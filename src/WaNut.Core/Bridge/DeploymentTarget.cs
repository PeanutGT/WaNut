namespace WaNut.Core.Bridge;

/// <summary>Where one server's bridge is deployed and how it is kept running.</summary>
/// <param name="Kind">This PC or a VPS.</param>
/// <param name="VpsId">The <c>VpsProfile.Id</c> for <see cref="DeploymentKind.Vps"/>; null for local.</param>
/// <param name="RunMode">The way to keep it running; <see cref="BridgeRunMode.Auto"/> by default.</param>
public sealed record DeploymentTarget(DeploymentKind Kind, string? VpsId = null, BridgeRunMode RunMode = BridgeRunMode.Auto)
{
    public static DeploymentTarget Local { get; } = new(DeploymentKind.Local);

    public static DeploymentTarget OnVps(string vpsId, BridgeRunMode runMode = BridgeRunMode.Auto) =>
        new(DeploymentKind.Vps, vpsId, runMode);

    /// <summary>False when a VPS target has no VPS, or the run mode belongs to the other kind of host.</summary>
    public bool IsValid => Kind switch
    {
        DeploymentKind.Local => VpsId is null && RunMode is BridgeRunMode.Auto or BridgeRunMode.DetachedProcess
            or BridgeRunMode.ScheduledTask or BridgeRunMode.WindowsService,
        DeploymentKind.Vps => !string.IsNullOrWhiteSpace(VpsId) && RunMode is BridgeRunMode.Auto or BridgeRunMode.Systemd
            or BridgeRunMode.Nohup or BridgeRunMode.Docker,
        _ => false,
    };
}
