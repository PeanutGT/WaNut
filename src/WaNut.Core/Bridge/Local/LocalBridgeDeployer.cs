using System.Net.NetworkInformation;
using WaNut.Core.Instances;
using WaNut.Core.Properties;
using WaNut.Core.Remote;
using WaNut.Core.Settings;

namespace WaNut.Core.Bridge.Local;

/// <summary>
/// Runs a server's bridge on this PC. Its files live in %APPDATA%\WaNut\Bridge\&lt;server id&gt;\: bridge.config.json with
/// the secrets DPAPI-encrypted, bridge.log, and bridge.run (how it runs). A background process by default (A1), a
/// sign-in task when the user wants it back after a restart (A2). A Windows service (A3) is not offered yet.
/// </summary>
public sealed class LocalBridgeDeployer : IBridgeDeployer
{
    public static readonly TimeSpan HealthDelay = TimeSpan.FromSeconds(3);

    public static readonly TimeSpan StopWait = TimeSpan.FromSeconds(5);

    public const int HealthTries = 5;

    private readonly LauncherPaths _paths;
    private readonly InstanceStore _store;
    private readonly IBridgeBinarySource _binaries;
    private readonly DiscordCheck _discord;
    private readonly ILocalBridgeHost _host;
    private readonly ISecretProtector _protector;
    private readonly Func<string, string, Task<BridgeStatus?>> _ask;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    /// <param name="ask">Asks a server's bridge over its pipe; <see cref="BridgeStatusPipe.AskAsync"/> by default.</param>
    public LocalBridgeDeployer(
        LauncherPaths paths,
        InstanceStore store,
        IBridgeBinarySource binaries,
        DiscordCheck discord,
        ILocalBridgeHost host,
        ISecretProtector protector,
        Func<string, string, Task<BridgeStatus?>>? ask = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _paths = paths;
        _store = store;
        _binaries = binaries;
        _discord = discord;
        _host = host;
        _protector = protector;
        _ask = ask ?? ((id, request) => BridgeStatusPipe.AskAsync(id, request));
        _delay = delay ?? Task.Delay;
    }

    public DeploymentKind Kind => DeploymentKind.Local;

    public string FolderOf(string serverId) => Path.Combine(_paths.Root, "Bridge", serverId);

    /// <summary>Auto means a background process: no admin rights and nothing left behind in Windows.</summary>
    public static BridgeRunMode Resolve(BridgeRunMode mode) => mode == BridgeRunMode.Auto ? BridgeRunMode.DetachedProcess : mode;

    public Task<BridgeDeployResult> DeployAsync(BridgeDeployRequest request, IProgress<BridgeDeployStep>? progress = null, CancellationToken cancellationToken = default) =>
        BridgeDeployRun.RunAsync(progress, async run =>
        {
            // Step 1: the request and Discord.
            run.Enter(BridgeDeployStep.Validate);
            var serverId = request.Config.ServerId;
            var mode = Resolve(request.Target.RunMode);
            if (request.Target.Kind != DeploymentKind.Local || !request.Target.IsValid || string.IsNullOrWhiteSpace(serverId)
                || serverId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || serverId is "." or ".." or "bin")
            {
                throw run.Fail(BridgeDeployError.InvalidRequest);
            }

            if (mode == BridgeRunMode.WindowsService)
            {
                throw run.Fail(BridgeDeployError.InvalidRequest, "A Windows service is not supported yet.");
            }

            var warnings = new List<BridgeDeployWarning>(await _discord.CheckAsync(request.Config, cancellationToken).ConfigureAwait(false));

            // Step 2: this PC always runs the Windows build.
            run.Enter(BridgeDeployStep.Probe);
            var propertiesPath = Path.Combine(request.ServerDirectory, GameSettingsStore.PropertiesFileName);
            if (!File.Exists(propertiesPath))
            {
                throw run.Fail(BridgeDeployError.ServerPropertiesMissing);
            }

            // Step 3: RCON on, only the keys it needs.
            run.Enter(BridgeDeployStep.PatchServerProperties);
            var original = await File.ReadAllBytesAsync(propertiesPath, cancellationToken).ConfigureAwait(false);
            var properties = PropertiesFile.Load(propertiesPath);
            var rcon = RconSetup.Apply(properties, PickRconPort(serverId));
            if (rcon.RestartNeeded)
            {
                properties.Save(propertiesPath);
                run.OnFailureSync(() => File.WriteAllBytes(propertiesPath, original));
            }

            // Step 4: the executable and the encrypted config. The bridge running now stops first, so its config can change.
            run.Enter(BridgeDeployStep.CopyFiles);
            var binary = await _binaries.GetAsync(BridgeBinaries.WinX64, cancellationToken).ConfigureAwait(false);
            var folder = FolderOf(serverId);
            Directory.CreateDirectory(folder);
            var previous = ReadRun(serverId);
            if (previous is not null)
            {
                var wasRunning = await StopRunAsync(serverId, previous, cancellationToken).ConfigureAwait(false);
                run.OnFailure(async () =>
                {
                    if (wasRunning)
                    {
                        await StartRunAsync(serverId, previous.Mode, previous.Executable, CancellationToken.None).ConfigureAwait(false);
                    }
                    else
                    {
                        WriteRun(serverId, previous);
                    }
                });
            }

            var configPath = Path.Combine(folder, BridgeConfigJson.FileName);
            var oldConfig = File.Exists(configPath) ? await File.ReadAllTextAsync(configPath, cancellationToken).ConfigureAwait(false) : null;
            var config = request.Config with
            {
                LogFile = Path.Combine(Path.GetFullPath(request.ServerDirectory), "logs", "latest.log"),
                RconPort = rcon.Port,
                RconPassword = rcon.Password,
            };
            AtomicFile.WriteAllText(configPath, BridgeConfigJson.Serialize(BridgeSecrets.Protect(config, _protector)));
            run.OnFailureSync(() =>
            {
                if (oldConfig is null)
                {
                    File.Delete(configPath);
                }
                else
                {
                    AtomicFile.WriteAllText(configPath, oldConfig);
                }
            });

            // Step 5: start it the chosen way.
            run.Enter(BridgeDeployStep.Register);
            var started = await StartRunAsync(serverId, mode, binary.Path, cancellationToken).ConfigureAwait(false);
            run.OnFailure(async () => await StopRunAsync(serverId, started, CancellationToken.None).ConfigureAwait(false));
            if (mode == BridgeRunMode.DetachedProcess)
            {
                warnings.Add(BridgeDeployWarning.NoStartOnBoot);
            }

            // Step 6: it must answer on its pipe.
            run.Enter(BridgeDeployStep.HealthCheck);
            var status = await WaitForAnswerAsync(serverId, started, cancellationToken).ConfigureAwait(false)
                ?? throw (started.ProcessId is { } pid && !_host.IsRunning(pid)
                    ? run.Fail(BridgeDeployError.BridgeExited, LastLogLines(serverId))
                    : run.Fail(BridgeDeployError.NoAnswer, LastLogLines(serverId)));
            if (started.ProcessId is null && status.ProcessId is { } answeredPid)
            {
                WriteRun(serverId, started with { ProcessId = answeredPid });
            }

            _binaries.DeleteOthers(ExecutablesInUse());
            return new BridgeDeployResult(status with { ServerRestartNeeded = rcon.RestartNeeded }, rcon.RestartNeeded, mode, warnings);
        });

    public async Task<BridgeStatus> GetStatusAsync(DeploymentTarget target, string serverId, CancellationToken cancellationToken = default)
    {
        var current = ReadRun(serverId);
        if (current is null)
        {
            return File.Exists(Path.Combine(FolderOf(serverId), BridgeConfigJson.FileName))
                ? new BridgeStatus { State = BridgeState.Stopped }
                : BridgeStatus.NotInstalled;
        }

        if (await _ask(serverId, BridgeStatusPipe.StatusRequest).ConfigureAwait(false) is { } answer)
        {
            return answer;
        }

        return current.ProcessId is { } pid && _host.IsRunning(pid)
            ? new BridgeStatus { State = BridgeState.Starting, ProcessId = pid }
            : new BridgeStatus { State = BridgeState.Stopped, Message = LastLogLines(serverId) };
    }

    public async Task StopAsync(DeploymentTarget target, string serverId, CancellationToken cancellationToken = default)
    {
        if (ReadRun(serverId) is { } current)
        {
            await StopRunAsync(serverId, current, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task RemoveAsync(DeploymentTarget target, string serverId, CancellationToken cancellationToken = default)
    {
        await StopAsync(target, serverId, cancellationToken).ConfigureAwait(false);
        await _host.DeleteTaskAsync(BridgeTaskXml.TaskName(serverId), cancellationToken).ConfigureAwait(false);
        var folder = FolderOf(serverId);
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }

        _binaries.DeleteOthers(ExecutablesInUse());
    }

    /// <summary>A port no other server on this PC uses, and that nothing listens on now.</summary>
    private int PickRconPort(string serverId)
    {
        var listening = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Select(e => e.Port).ToHashSet();
        return BridgePorts.Pick(_store, serverId, i => i.VpsId is null, port => !listening.Contains(port));
    }

    private async Task<LocalBridgeRun> StartRunAsync(string serverId, BridgeRunMode mode, string executable, CancellationToken cancellationToken)
    {
        var folder = FolderOf(serverId);
        IReadOnlyList<string> arguments =
            ["--config", Path.Combine(folder, BridgeConfigJson.FileName), "--log", Path.Combine(folder, "bridge.log")];
        LocalBridgeRun started;
        if (mode == BridgeRunMode.ScheduledTask)
        {
            await _host.RegisterTaskAsync(BridgeTaskXml.TaskName(serverId), executable, arguments, cancellationToken).ConfigureAwait(false);
            started = new LocalBridgeRun(mode, executable);
        }
        else
        {
            started = new LocalBridgeRun(mode, executable, _host.Start(executable, arguments));
        }

        WriteRun(serverId, started);
        return started;
    }

    /// <summary>Asks the bridge to stop, then ends it if it doesn't. True when one was running.</summary>
    private async Task<bool> StopRunAsync(string serverId, LocalBridgeRun current, CancellationToken cancellationToken)
    {
        var answer = await _ask(serverId, BridgeStatusPipe.StopRequest).ConfigureAwait(false);
        var pid = answer?.ProcessId ?? current.ProcessId;
        var running = answer is not null || (pid is { } p && _host.IsRunning(p));
        if (pid is { } id)
        {
            for (var waited = TimeSpan.Zero; _host.IsRunning(id) && waited < StopWait; waited += TimeSpan.FromMilliseconds(250))
            {
                await _delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
            }

            _host.Kill(id);
        }

        if (current.Mode == BridgeRunMode.ScheduledTask)
        {
            await _host.EndTaskAsync(BridgeTaskXml.TaskName(serverId), cancellationToken).ConfigureAwait(false);
            await _host.DeleteTaskAsync(BridgeTaskXml.TaskName(serverId), cancellationToken).ConfigureAwait(false);
        }

        File.Delete(Path.Combine(FolderOf(serverId), LocalBridgeRun.FileName));
        return running;
    }

    private async Task<BridgeStatus?> WaitForAnswerAsync(string serverId, LocalBridgeRun started, CancellationToken cancellationToken)
    {
        await _delay(HealthDelay, cancellationToken).ConfigureAwait(false);
        for (var attempt = 1; ; attempt++)
        {
            if (await _ask(serverId, BridgeStatusPipe.StatusRequest).ConfigureAwait(false) is { } status)
            {
                return status;
            }

            if (attempt == HealthTries || (started.ProcessId is { } pid && !_host.IsRunning(pid)))
            {
                return null;
            }

            await _delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }
    }

    private LocalBridgeRun? ReadRun(string serverId)
    {
        var path = Path.Combine(FolderOf(serverId), LocalBridgeRun.FileName);
        return File.Exists(path) ? LocalBridgeRun.Parse(File.ReadAllText(path)) : null;
    }

    private void WriteRun(string serverId, LocalBridgeRun run) =>
        AtomicFile.WriteAllText(Path.Combine(FolderOf(serverId), LocalBridgeRun.FileName), run.ToText());

    /// <summary>Executables a task or process of any server still points at, so a build swap never removes them.</summary>
    private List<string> ExecutablesInUse()
    {
        var root = Path.Combine(_paths.Root, "Bridge");
        return !Directory.Exists(root) ? []
            : Directory.EnumerateFiles(root, LocalBridgeRun.FileName, SearchOption.AllDirectories)
                .Select(f => LocalBridgeRun.Parse(File.ReadAllText(f))?.Executable)
                .OfType<string>()
                .ToList();
    }

    public Task<IReadOnlyList<string>> GetRecentLogAsync(DeploymentTarget target, string serverId, int lines = 40, CancellationToken cancellationToken = default) =>
        Task.FromResult(TailLog(serverId, lines));

    /// <summary>The end of bridge.log, for a failure message. The bridge never logs secrets.</summary>
    private string? LastLogLines(string serverId, int count = 15)
    {
        var lines = TailLog(serverId, count);
        return lines.Count == 0 ? null : string.Join('\n', lines);
    }

    /// <summary>Reads only the last part of the file, since the log keeps growing while the bridge runs.</summary>
    private IReadOnlyList<string> TailLog(string serverId, int count)
    {
        var path = Path.Combine(FolderOf(serverId), "bridge.log");
        try
        {
            if (!File.Exists(path))
            {
                return [];
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            const int Window = 64 * 1024;
            stream.Seek(Math.Max(0, stream.Length - Window), SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            return [.. reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).TakeLast(count)];
        }
        catch (IOException)
        {
            return [];
        }
    }
}
