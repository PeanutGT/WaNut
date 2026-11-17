namespace WaNut.Core.Bridge;

/// <summary>Where the Discord bridge process lives.</summary>
public enum DeploymentKind
{
    /// <summary>On this PC, next to the server.</summary>
    Local,

    /// <summary>On the user's own Linux VPS, reached over SSH.</summary>
    Vps,
}
