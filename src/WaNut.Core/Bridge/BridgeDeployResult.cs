namespace WaNut.Core.Bridge;

/// <param name="Status">The health check's answer.</param>
/// <param name="ServerRestartNeeded">server.properties changed, so the Minecraft server must restart once to open RCON.</param>
public sealed record BridgeDeployResult(BridgeStatus Status, bool ServerRestartNeeded);
