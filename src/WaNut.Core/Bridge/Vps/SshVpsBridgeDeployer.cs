using Renci.SshNet;
using Renci.SshNet.Common;
using WaNut.Core.Instances;
using WaNut.Core.Properties;
using WaNut.Core.Remote;
using WaNut.Core.Remote.Firewall;
using WaNut.Core.Remote.Run;
using WaNut.Core.Remote.Sync;
using WaNut.Core.Settings;

namespace WaNut.Core.Bridge.Vps;

/// <summary>
/// Runs a server's bridge on the VPS the server runs on, over the same SSH connection code as the server itself
/// (<see cref="VpsConnector"/>, <see cref="HostProbe"/>, <see cref="HostFirewall"/>, <see cref="VpsSettingsSync"/>).
/// systemd service when the account has password-less sudo (B1), else a background loop (B2). Docker (B3) is not
/// offered yet.
/// </summary>
public sealed class SshVpsBridgeDeployer(
    VpsConnector connector,
    Func<string, VpsProfile?> findVps,
    InstanceStore store,
    VpsSettingsSync settingsSync,
    IBridgeBinarySource binaries,
    DiscordCheck discord,
    Func<TimeSpan, CancellationToken, Task>? delay = null) : IBridgeDeployer
{
    public static readonly TimeSpan HealthDelay = TimeSpan.FromSeconds(3);

    public const int HealthTries = 5;

    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;

    public DeploymentKind Kind => DeploymentKind.Vps;

    public async Task<BridgeDeployResult> DeployAsync(BridgeDeployRequest request, IProgress<BridgeDeployStep>? progress = null, CancellationToken cancellationToken = default)
    {
        // Closed only after the rollback, which needs the connection too.
        SshClient? connection = null;
        try
        {
            return await DeployAsync(request, progress, client => connection = client, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            connection?.Dispose();
        }
    }

    private Task<BridgeDeployResult> DeployAsync(BridgeDeployRequest request, IProgress<BridgeDeployStep>? progress, Action<SshClient> keep, CancellationToken cancellationToken) =>
        BridgeDeployRun.RunAsync(progress, async run =>
        {
            // Step 1: the request, Discord, and the VPS answers.
            run.Enter(BridgeDeployStep.Validate);
            var serverId = request.Config.ServerId;
            if (request.Target.Kind != DeploymentKind.Vps || !request.Target.IsValid || !IsUsableId(serverId))
            {
                throw run.Fail(BridgeDeployError.InvalidRequest);
            }

            if (request.Target.RunMode == BridgeRunMode.Docker)
            {
                throw run.Fail(BridgeDeployError.InvalidRequest, "Docker is not supported yet.");
            }

            var vps = findVps(request.Target.VpsId!) ?? throw run.Fail(BridgeDeployError.InvalidRequest, "No such VPS.");
            var metadata = store.LoadAll().FirstOrDefault(i => i.Id == serverId) ?? throw run.Fail(BridgeDeployError.InvalidRequest, "No such server.");
            var warnings = new List<BridgeDeployWarning>(await discord.CheckAsync(request.Config, cancellationToken).ConfigureAwait(false));
            var ssh = await ConnectAsync(run, vps, cancellationToken).ConfigureAwait(false);
            keep(ssh);

            // Step 2: which build, and which way to keep it running.
            run.Enter(BridgeDeployStep.Probe);
            var host = HostProbe.Parse(await RunAsync(ssh, HostProbe.Script, cancellationToken).ConfigureAwait(false));
            var rid = host.Os.Equals("Linux", StringComparison.OrdinalIgnoreCase) ? BridgeBinaries.RidFor(host.Architecture) : null;
            if (rid is null)
            {
                throw run.Fail(BridgeDeployError.UnsupportedHost, $"{host.Os} {host.Machine}".Trim());
            }

            var mode = ReadKeys(await RunAsync(ssh, VpsBridgeScripts.ModeScript, cancellationToken).ConfigureAwait(false));
            var (home, user, group) = (mode.GetValueOrDefault("home", string.Empty), mode.GetValueOrDefault("user", string.Empty), mode.GetValueOrDefault("group", string.Empty));
            var canSystemd = mode.GetValueOrDefault("mode") == "systemd" && RunnerScript.IsPlainUserName(user);
            if (!home.StartsWith('/') || user.Length == 0)
            {
                throw run.Fail(BridgeDeployError.UnsupportedHost, "Could not read the account's home folder.");
            }

            var runMode = request.Target.RunMode switch
            {
                BridgeRunMode.Auto => canSystemd ? BridgeRunMode.Systemd : BridgeRunMode.Nohup,
                BridgeRunMode.Systemd when !canSystemd => throw run.Fail(BridgeDeployError.UnsupportedHost, "systemd with password-less sudo is needed."),
                var chosen => chosen,
            };
            var systemd = runMode == BridgeRunMode.Systemd;
            var basePath = VpsBridgeScripts.BaseFor(systemd, home);
            var serverRoot = SftpSyncTree.ServerRoot(serverId);

            // Step 3: RCON on, through the safe settings path when the newest files are on the VPS.
            run.Enter(BridgeDeployStep.PatchServerProperties);
            var rcon = await PatchAsync(run, ssh, vps, metadata, serverRoot, cancellationToken).ConfigureAwait(false);
            warnings.AddRange(await CheckFirewallAsync(ssh, host.Provider, rcon.Port, cancellationToken).ConfigureAwait(false));

            // Step 4: the executable (only when it changed) and the config, chmod 600.
            run.Enter(BridgeDeployStep.CopyFiles);
            var binary = await binaries.GetAsync(rid, cancellationToken).ConfigureAwait(false);
            await RunAsync(ssh, VpsBridgeScripts.PrepareFolders(systemd, basePath, serverId, user, group), cancellationToken).ConfigureAwait(false);
            if ((await RunAsync(ssh, VpsBridgeScripts.HashCommand(basePath), cancellationToken).ConfigureAwait(false)).Trim() != binary.Sha256)
            {
                await UploadAsync(vps, binary.Path, $"{basePath}/{VpsBridgeScripts.Executable}.part", cancellationToken).ConfigureAwait(false);
                await RunAsync(ssh, VpsBridgeScripts.InstallExecutable(basePath), cancellationToken).ConfigureAwait(false);
            }

            // A bridge running now keeps its old config until Register restarts it; if this deployment fails, it runs again.
            var wasRunning = (await RunAsync(ssh, VpsBridgeScripts.Stop(home, serverId), cancellationToken).ConfigureAwait(false)).Contains("was-running", StringComparison.Ordinal);
            var previousSystemd = (await RunAsync(ssh, VpsBridgeScripts.Status(home, serverId), cancellationToken).ConfigureAwait(false)).StartsWith("unit", StringComparison.Ordinal);
            run.OnFailure(async () =>
            {
                if (wasRunning)
                {
                    await StartAsync(ssh, previousSystemd, home, serverId, user, CancellationToken.None).ConfigureAwait(false);
                }
            });

            var config = request.Config with
            {
                LogFile = $"{home}/{serverRoot}/logs/latest.log",
                RconPort = rcon.Port,
                RconPassword = rcon.Password,
            };
            await VpsConnector.RunWithInputAsync(ssh, VpsBridgeScripts.Sh(VpsBridgeScripts.WriteConfig(basePath, serverId)), BridgeConfigJson.Serialize(config), cancellationToken).ConfigureAwait(false);
            run.OnFailure(() => RunAsync(ssh, VpsBridgeScripts.RestoreConfig(basePath, serverId), CancellationToken.None));

            // Step 5: the service or the loop.
            run.Enter(BridgeDeployStep.Register);
            if (!systemd && previousSystemd)
            {
                // Moving from a service to the loop: the old unit must not start a second bridge at boot.
                await RunAsync(ssh, VpsBridgeScripts.DeleteUnit(serverId), cancellationToken).ConfigureAwait(false);
            }

            var unitExisted = await StartAsync(ssh, systemd, home, serverId, user, cancellationToken).ConfigureAwait(false);
            run.OnFailure(() => RunAsync(ssh, systemd && !unitExisted ? VpsBridgeScripts.DeleteUnit(serverId) : VpsBridgeScripts.Stop(home, serverId), CancellationToken.None));
            if (!systemd)
            {
                warnings.Add(BridgeDeployWarning.NoStartOnBoot);
            }

            // Step 6: it must answer.
            run.Enter(BridgeDeployStep.HealthCheck);
            var status = await WaitForAnswerAsync(ssh, home, serverId, cancellationToken).ConfigureAwait(false);
            if (status.State != BridgeState.Running)
            {
                var log = (await RunAsync(ssh, VpsBridgeScripts.LastLog(systemd, basePath, serverId), CancellationToken.None).ConfigureAwait(false)).Trim();
                throw run.Fail(status.State == BridgeState.Starting ? BridgeDeployError.NoAnswer : BridgeDeployError.BridgeExited, log.Length > 0 ? log : null);
            }

            await RunAsync(ssh, VpsBridgeScripts.DropConfigBackup(basePath, serverId), CancellationToken.None).ConfigureAwait(false);
            return new BridgeDeployResult(status with { ServerRestartNeeded = rcon.RestartNeeded }, rcon.RestartNeeded, runMode, warnings);
        });

    public async Task<BridgeStatus> GetStatusAsync(DeploymentTarget target, string serverId, CancellationToken cancellationToken = default)
    {
        if (!IsUsableId(serverId) || target.VpsId is null || findVps(target.VpsId) is not { } vps)
        {
            return BridgeStatus.NotInstalled;
        }

        try
        {
            using var ssh = await connector.ConnectAsync(vps, cancellationToken: cancellationToken).ConfigureAwait(false);
            var home = await HomeAsync(ssh, cancellationToken).ConfigureAwait(false);
            return ParseStatus(await RunAsync(ssh, VpsBridgeScripts.Status(home, serverId), cancellationToken).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is VpsUnreachableException or VpsLoginException or HostKeyException or SshException or InvalidOperationException)
        {
            return new BridgeStatus { State = BridgeState.Unknown, Message = ex.Message };
        }
    }

    public async Task<IReadOnlyList<string>> GetRecentLogAsync(DeploymentTarget target, string serverId, int lines = 40, CancellationToken cancellationToken = default)
    {
        if (!IsUsableId(serverId) || target.VpsId is null || findVps(target.VpsId) is not { } vps)
        {
            return [];
        }

        try
        {
            using var ssh = await connector.ConnectAsync(vps, cancellationToken: cancellationToken).ConfigureAwait(false);
            var home = await HomeAsync(ssh, cancellationToken).ConfigureAwait(false);
            var output = await RunAsync(ssh, VpsBridgeScripts.RecentLog(home, serverId, lines), cancellationToken).ConfigureAwait(false);
            return [.. output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
        }
        catch (Exception ex) when (ex is VpsUnreachableException or VpsLoginException or HostKeyException or SshException or InvalidOperationException)
        {
            return [];
        }
    }

    public async Task StopAsync(DeploymentTarget target, string serverId, CancellationToken cancellationToken = default)
    {
        if (IsUsableId(serverId) && target.VpsId is not null && findVps(target.VpsId) is { } vps)
        {
            using var ssh = await connector.ConnectAsync(vps, cancellationToken: cancellationToken).ConfigureAwait(false);
            await RunAsync(ssh, VpsBridgeScripts.Stop(await HomeAsync(ssh, cancellationToken).ConfigureAwait(false), serverId), cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task RemoveAsync(DeploymentTarget target, string serverId, CancellationToken cancellationToken = default)
    {
        if (IsUsableId(serverId) && target.VpsId is not null && findVps(target.VpsId) is { } vps)
        {
            using var ssh = await connector.ConnectAsync(vps, cancellationToken: cancellationToken).ConfigureAwait(false);
            var home = await HomeAsync(ssh, cancellationToken).ConfigureAwait(false);
            await RunAsync(ssh, VpsBridgeScripts.Stop(home, serverId), cancellationToken).ConfigureAwait(false);
            await RunAsync(ssh, VpsBridgeScripts.Remove(home, serverId), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Reads the status script's answer.</summary>
    public static BridgeStatus ParseStatus(string output)
    {
        var line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? string.Empty;
        if (line.StartsWith('{'))
        {
            try
            {
                return BridgeStatusPipe.Deserialize(line);
            }
            catch (System.Text.Json.JsonException)
            {
                return new BridgeStatus { State = BridgeState.Unknown, Message = line };
            }
        }

        return line switch
        {
            "none" => BridgeStatus.NotInstalled,
            "unit active" or "unit activating" or "unit reloading" or "plain running" => new BridgeStatus { State = BridgeState.Starting },
            "unit failed" => new BridgeStatus { State = BridgeState.Failed },
            _ => new BridgeStatus { State = BridgeState.Stopped },
        };
    }

    private static bool IsUsableId(string id) =>
        !string.IsNullOrWhiteSpace(id) && id is not ("." or "..") && id.IndexOfAny(['/', '\\', '\0', '\n', '\r']) < 0;

    private async Task<SshClient> ConnectAsync(BridgeDeployRun run, VpsProfile vps, CancellationToken cancellationToken)
    {
        try
        {
            return await connector.ConnectAsync(vps, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is VpsUnreachableException or VpsLoginException or HostKeyException)
        {
            throw run.Fail(BridgeDeployError.VpsUnreachable, ex.Message, ex);
        }
    }

    /// <summary>
    /// Files on the VPS (<see cref="InstanceMetadata.VpsCopyIsNewer"/>): bring its server.properties here, patch, send it
    /// back, only while the server is stopped there. Otherwise this PC's copy is the newest and goes up with the next start.
    /// </summary>
    private async Task<RconSetupResult> PatchAsync(BridgeDeployRun run, SshClient ssh, VpsProfile vps, InstanceMetadata metadata, string serverRoot, CancellationToken cancellationToken)
    {
        var localDir = store.InstanceDir(metadata.Id);
        var path = Path.Combine(localDir, GameSettingsStore.PropertiesFileName);
        byte[]? original = File.Exists(path) ? await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false) : null;
        var remote = metadata.VpsCopyIsNewer;
        var running = remote && (await RunAsync(ssh, RunnerScript.ProbeCommand(serverRoot), cancellationToken).ConfigureAwait(false)).StartsWith("running", StringComparison.Ordinal);
        if (remote)
        {
            run.OnFailureSync(() => RestoreLocal(path, original));
            try
            {
                await settingsSync.PullAsync(metadata.Id, vps, cancellationToken).ConfigureAwait(false);
            }
            catch (VpsFolderMissingException ex)
            {
                throw run.Fail(BridgeDeployError.ServerPropertiesMissing, null, ex);
            }
        }
        else if (original is null)
        {
            throw run.Fail(BridgeDeployError.ServerPropertiesMissing);
        }

        var properties = PropertiesFile.Load(path);
        var rcon = RconSetup.Apply(properties, BridgePorts.Pick(store, metadata.Id, i => i.VpsId == metadata.VpsId, _ => true));
        if (!rcon.RestartNeeded)
        {
            return rcon;
        }

        if (running)
        {
            throw run.Fail(BridgeDeployError.ServerRunning);
        }

        properties.Save(path);
        if (remote)
        {
            // Pushed only when the pull worked, so restoring this PC's copy and pushing it again puts the VPS back too.
            run.OnFailure(async () =>
            {
                RestoreLocal(path, original);
                await settingsSync.PushAsync(metadata.Id, vps, CancellationToken.None).ConfigureAwait(false);
            });
            await settingsSync.PushAsync(metadata.Id, vps, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            run.OnFailureSync(() => RestoreLocal(path, original));
        }

        return rcon;
    }

    private static void RestoreLocal(string path, byte[]? original)
    {
        if (original is not null)
        {
            File.WriteAllBytes(path, original);
        }
    }

    /// <summary>Peanut's choice: RCON stays on rcon.ip=127.0.0.1 where honored, and the VPS firewall must keep its port closed.</summary>
    private static async Task<IReadOnlyList<BridgeDeployWarning>> CheckFirewallAsync(SshClient ssh, VpsProvider provider, int port, CancellationToken cancellationToken)
    {
        var firewall = HostFirewall.Parse(await VpsConnector.RunAsync(ssh, HostFirewall.Command, cancellationToken).ConfigureAwait(false), provider);
        return firewall.IsOpen(port) switch
        {
            true => [BridgeDeployWarning.RconPortOpen],
            null => [BridgeDeployWarning.RconPortUnchecked],
            false => [],
        };
    }

    private async Task UploadAsync(VpsProfile vps, string localPath, string remotePath, CancellationToken cancellationToken)
    {
        using var sftp = await connector.ConnectSftpAsync(vps, cancellationToken).ConfigureAwait(false);
        await using var file = File.OpenRead(localPath);
        await sftp.UploadFileAsync(file, remotePath, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Starts the service or the loop; true when the service's unit file was already there.</summary>
    private static async Task<bool> StartAsync(SshClient ssh, bool systemd, string home, string serverId, string user, CancellationToken cancellationToken)
    {
        if (!systemd)
        {
            await RunAsync(ssh, VpsBridgeScripts.StartPlain(VpsBridgeScripts.BaseFor(false, home), serverId), cancellationToken).ConfigureAwait(false);
            return false;
        }

        var output = await VpsConnector.RunWithInputAsync(
            ssh,
            VpsBridgeScripts.Sh(VpsBridgeScripts.StartUnit(serverId)),
            VpsBridgeScripts.Unit(VpsBridgeScripts.SystemBase, serverId, user),
            cancellationToken).ConfigureAwait(false);
        return output.Contains("existed", StringComparison.Ordinal);
    }

    private async Task<BridgeStatus> WaitForAnswerAsync(SshClient ssh, string home, string serverId, CancellationToken cancellationToken)
    {
        await _delay(HealthDelay, cancellationToken).ConfigureAwait(false);
        for (var attempt = 1; ; attempt++)
        {
            var status = ParseStatus(await RunAsync(ssh, VpsBridgeScripts.Status(home, serverId), cancellationToken).ConfigureAwait(false));
            if (status.State is not BridgeState.Starting || attempt == HealthTries)
            {
                return status;
            }

            await _delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<string> HomeAsync(SshClient ssh, CancellationToken cancellationToken) =>
        ReadKeys(await RunAsync(ssh, VpsBridgeScripts.ModeScript, cancellationToken).ConfigureAwait(false)).GetValueOrDefault("home", string.Empty);

    private static Task<string> RunAsync(SshClient ssh, string script, CancellationToken cancellationToken) =>
        VpsConnector.RunAsync(ssh, VpsBridgeScripts.Sh(script), cancellationToken);

    private static Dictionary<string, string> ReadKeys(string output) =>
        output.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => l.IndexOf('=') > 0)
            .GroupBy(l => l[..l.IndexOf('=')])
            .ToDictionary(g => g.Key, g => g.Last()[(g.Key.Length + 1)..].Trim());
}
