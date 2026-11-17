namespace WaNut.Core.Bridge;

/// <param name="Status">The health check's answer.</param>
/// <param name="ServerRestartNeeded">server.properties changed, so the Minecraft server must restart once to open RCON.</param>
/// <param name="RunMode">How the bridge is kept running, as picked from <see cref="DeploymentTarget.RunMode"/>.</param>
/// <param name="Warnings">What still needs the user's attention; empty when nothing does.</param>
public sealed record BridgeDeployResult(
    BridgeStatus Status,
    bool ServerRestartNeeded,
    BridgeRunMode RunMode = BridgeRunMode.Auto,
    IReadOnlyList<BridgeDeployWarning>? Warnings = null)
{
    public IReadOnlyList<BridgeDeployWarning> Warnings { get; init; } = Warnings ?? [];
}
