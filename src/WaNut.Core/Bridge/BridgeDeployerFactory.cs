namespace WaNut.Core.Bridge;

/// <summary>Picks the deployer for a target (strategy pattern): one per <see cref="DeploymentKind"/>.</summary>
public sealed class BridgeDeployerFactory(IEnumerable<IBridgeDeployer> deployers)
{
    private readonly Dictionary<DeploymentKind, IBridgeDeployer> _deployers = deployers.ToDictionary(d => d.Kind);

    /// <exception cref="NotSupportedException">No deployer handles that kind of host.</exception>
    public IBridgeDeployer For(DeploymentTarget target) =>
        _deployers.TryGetValue(target.Kind, out var deployer) ? deployer : throw new NotSupportedException($"No bridge deployer for {target.Kind}.");
}
