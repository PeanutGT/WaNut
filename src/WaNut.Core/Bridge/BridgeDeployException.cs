namespace WaNut.Core.Bridge;

/// <summary>A deployment failed in <see cref="Step"/>; everything earlier steps changed has been undone.</summary>
public sealed class BridgeDeployException(BridgeDeployStep step, BridgeDeployError error, string? detail = null, Exception? inner = null)
    : Exception($"Bridge deployment failed at {step}: {error}{(detail is null ? string.Empty : " (" + detail + ")")}", inner)
{
    public BridgeDeployStep Step { get; } = step;

    public BridgeDeployError Error { get; } = error;

    /// <summary>Text for the user that goes with the error, such as missing permission names or the bridge's last log lines. Never a secret.</summary>
    public string? Detail { get; } = detail;
}
