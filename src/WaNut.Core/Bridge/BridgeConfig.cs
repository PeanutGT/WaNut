namespace WaNut.Core.Bridge;

/// <summary>
/// Everything the bridge process needs, written next to it as <c>bridge.config.json</c>. Holds secrets
/// (<see cref="BotToken"/>, <see cref="WebhookUrl"/>, <see cref="RconPassword"/>), so a deployer must protect the
/// file: DPAPI on this PC, <c>chmod 600</c> on a VPS. Ids are strings because a Discord snowflake does not fit a
/// JSON number safely.
/// </summary>
public sealed record BridgeConfig
{
    public const int CurrentSchemaVersion = 1;

    /// <summary>Lets a newer bridge read or reject an older file.</summary>
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>The WaNut server this bridge belongs to.</summary>
    public string ServerId { get; init; } = string.Empty;

    /// <summary>The server's <c>logs/latest.log</c>, as the bridge sees it (a VPS path for a VPS deployment).</summary>
    public string LogFile { get; init; } = string.Empty;

    public int RconPort { get; init; } = RconSetup.DefaultPort;

    public string RconPassword { get; init; } = string.Empty;

    public string? BotToken { get; init; }

    public string? ChannelId { get; init; }

    /// <summary>Needed by <see cref="DiscordPresentation.Webhook"/>.</summary>
    public string? WebhookUrl { get; init; }

    public DiscordPresentation Presentation { get; init; } = DiscordPresentation.Webhook;

    /// <summary>The Discord role allowed to use the remote console when <see cref="BridgeFilters.RemoteConsole"/> is on.</summary>
    public string? AdminRoleId { get; init; }

    public BridgeFilters Filters { get; init; } = new();
}
