namespace WaNut.Core.Bridge;

/// <summary>
/// Everything the bridge process needs, written next to it as <c>bridge.config.json</c>. Holds secrets
/// (<see cref="BotToken"/>, <see cref="WebhookUrl"/>, <see cref="RconPassword"/>), so a deployer must protect the
/// file: DPAPI on this PC, <c>chmod 600</c> on a VPS. Ids are strings because a Discord snowflake does not fit a
/// JSON number safely. Properties have setters, not init: the generated JSON reader (needed by the trimmed
/// bridge) resets a missing init-only property to its type's default instead of keeping the initializer.
/// </summary>
public sealed record BridgeConfig
{
    public const int CurrentSchemaVersion = 1;

    /// <summary>Lets a newer bridge read or reject an older file.</summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>The WaNut server this bridge belongs to.</summary>
    public string ServerId { get; set; } = string.Empty;

    /// <summary>The server's <c>logs/latest.log</c>, as the bridge sees it (a VPS path for a VPS deployment).</summary>
    public string LogFile { get; set; } = string.Empty;

    public int RconPort { get; set; } = RconSetup.DefaultPort;

    public string RconPassword { get; set; } = string.Empty;

    public string? BotToken { get; set; }

    public string? ChannelId { get; set; }

    /// <summary>Needed by <see cref="DiscordPresentation.Webhook"/>.</summary>
    public string? WebhookUrl { get; set; }

    public DiscordPresentation Presentation { get; set; } = DiscordPresentation.Webhook;

    /// <summary>
    /// The language of the bridge's own words in Discord ("en" or "zh-TW"). Chat and the server's death and advancement
    /// lines are passed on as written.
    /// </summary>
    public string Language { get; set; } = "en";

    /// <summary>The Discord role allowed to use the remote console when <see cref="BridgeFilters.RemoteConsole"/> is on.</summary>
    public string? AdminRoleId { get; set; }

    public BridgeFilters Filters { get; set; } = new();
}
