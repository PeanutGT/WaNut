namespace WaNut.Core.Bridge;

/// <summary>
/// What the user filled in on the Discord tab for one server, kept on this PC so the form comes back as it was.
/// The RCON fields and the log path are not kept: the deployer fills those in from the server each time.
/// </summary>
public sealed record BridgeSettings
{
    /// <summary>This PC only: bring the bridge back after Windows restarts (a sign-in task) instead of a plain background process.</summary>
    public bool StartOnBoot { get; set; }

    public BridgeConfig Config { get; set; } = new();
}
