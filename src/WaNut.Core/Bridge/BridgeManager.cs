using WaNut.Core.Instances;

namespace WaNut.Core.Bridge;

/// <summary>
/// What the Discord tab asks for, in one place: picks the deployer for the server's host, keeps the form's settings
/// and runs the deployment. The bridge goes where the server runs, because it follows that server's own log file.
/// </summary>
public sealed class BridgeManager(BridgeDeployerFactory deployers, BridgeSettingsStore settings, DiscordCheck discord, InstanceStore store)
{
    /// <summary>A server on a VPS gets its bridge on the same VPS; a server on this PC gets it here.</summary>
    public static DeploymentTarget TargetFor(InstanceMetadata server, bool startOnBoot) =>
        server.VpsId is { } vps
            ? DeploymentTarget.OnVps(vps)
            : new DeploymentTarget(DeploymentKind.Local, null, startOnBoot ? BridgeRunMode.ScheduledTask : BridgeRunMode.DetachedProcess);

    /// <summary>The saved form, or the defaults for a server that never had a bridge.</summary>
    public BridgeSettings Load(string serverId) =>
        settings.Load(serverId) ?? new BridgeSettings { Config = new BridgeConfig { ServerId = serverId } };

    public void Save(string serverId, BridgeSettings value) => settings.Save(serverId, value);

    /// <summary>Step 1 of a deployment on its own: the token, channel permissions and webhook, with nothing changed anywhere.</summary>
    /// <exception cref="BridgeDeployException">Something can't work; its error says what.</exception>
    public Task<IReadOnlyList<BridgeDeployWarning>> TestAsync(BridgeConfig config, CancellationToken cancellationToken = default) =>
        discord.CheckAsync(config, cancellationToken);

    /// <exception cref="BridgeDeployException">A step failed and was undone.</exception>
    public async Task<BridgeDeployResult> DeployAsync(
        InstanceMetadata server, BridgeSettings value, IProgress<BridgeDeployStep>? progress = null, CancellationToken cancellationToken = default)
    {
        var config = value.Config with { ServerId = server.Id };
        var target = TargetFor(server, value.StartOnBoot);
        settings.Save(server.Id, value with { Config = config });
        return await deployers.For(target)
            .DeployAsync(new BridgeDeployRequest(target, config, store.InstanceDir(server.Id)), progress, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<BridgeStatus> StatusAsync(InstanceMetadata server, CancellationToken cancellationToken = default) =>
        deployers.For(TargetFor(server, false)).GetStatusAsync(TargetFor(server, false), server.Id, cancellationToken);

    public Task<IReadOnlyList<string>> LogAsync(InstanceMetadata server, int lines = 40, CancellationToken cancellationToken = default) =>
        deployers.For(TargetFor(server, false)).GetRecentLogAsync(TargetFor(server, false), server.Id, lines, cancellationToken);

    public Task StopAsync(InstanceMetadata server, CancellationToken cancellationToken = default) =>
        deployers.For(TargetFor(server, false)).StopAsync(TargetFor(server, false), server.Id, cancellationToken);

    /// <summary>Removes the bridge from its host. The saved form stays, so deploying again is one click.</summary>
    public Task RemoveAsync(InstanceMetadata server, CancellationToken cancellationToken = default) =>
        deployers.For(TargetFor(server, false)).RemoveAsync(TargetFor(server, false), server.Id, cancellationToken);

    /// <summary>
    /// For a server that was just deleted: takes its bridge off the host and forgets the saved form. Does nothing for a
    /// server that never had one. Best effort, because the folder it followed is gone either way.
    /// </summary>
    public async Task ForgetAsync(InstanceMetadata server)
    {
        if (settings.Load(server.Id) is null)
        {
            return;
        }

        try
        {
            await RemoveAsync(server).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A VPS that can't be reached now keeps a bridge that finds no log file; the user can remove it by hand.
        }

        settings.Delete(server.Id);
    }
}
