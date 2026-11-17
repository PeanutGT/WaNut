namespace WaNut.Core.Bridge;

public enum BridgeState
{
    /// <summary>Nothing deployed for this server.</summary>
    NotInstalled,

    Stopped,

    Starting,

    Running,

    /// <summary>It was deployed but exited or could not start; see <see cref="BridgeStatus.Message"/>.</summary>
    Failed,

    /// <summary>The host could not be reached to ask.</summary>
    Unknown,
}
