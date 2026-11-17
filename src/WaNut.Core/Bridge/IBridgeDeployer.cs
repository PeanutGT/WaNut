namespace WaNut.Core.Bridge;

/// <summary>
/// Puts the bridge on one kind of host and keeps it running there. One implementation per
/// <see cref="DeploymentKind"/>; the app picks one from the user's <see cref="DeploymentTarget"/>.
/// </summary>
public interface IBridgeDeployer
{
    DeploymentKind Kind { get; }

    /// <summary>
    /// Runs the whole pipeline: validate, probe the host, patch server.properties (see <see cref="RconSetup"/>),
    /// copy the executable and config, register the background service and check that it came up.
    /// </summary>
    Task<BridgeDeployResult> DeployAsync(
        BridgeDeployRequest request,
        IProgress<BridgeDeployStep>? progress = null,
        CancellationToken cancellationToken = default);

    Task<BridgeStatus> GetStatusAsync(DeploymentTarget target, string serverId, CancellationToken cancellationToken = default);

    /// <summary>The end of the bridge's own log, oldest line first; empty when there is none or the host can't be reached. The bridge never logs secrets.</summary>
    Task<IReadOnlyList<string>> GetRecentLogAsync(DeploymentTarget target, string serverId, int lines = 40, CancellationToken cancellationToken = default);

    /// <summary>Stops the bridge but keeps its files and config.</summary>
    Task StopAsync(DeploymentTarget target, string serverId, CancellationToken cancellationToken = default);

    /// <summary>Stops the bridge and deletes what <see cref="DeployAsync"/> put on the host. server.properties is left as patched.</summary>
    Task RemoveAsync(DeploymentTarget target, string serverId, CancellationToken cancellationToken = default);
}
