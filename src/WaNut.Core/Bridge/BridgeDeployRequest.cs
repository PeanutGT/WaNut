namespace WaNut.Core.Bridge;

/// <summary>What to deploy and where.</summary>
/// <param name="Target">This PC or a VPS.</param>
/// <param name="Config">The bridge settings. Its RCON fields are filled in by the deployer from the patched server.properties.</param>
/// <param name="ServerDirectory">The server folder that holds <c>server.properties</c>, as the target host sees it.</param>
public sealed record BridgeDeployRequest(DeploymentTarget Target, BridgeConfig Config, string ServerDirectory);
