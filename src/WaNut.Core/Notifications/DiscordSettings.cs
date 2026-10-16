namespace WaNut.Core.Notifications;

/// <summary>Which server events are posted to Discord, and where. Off until a webhook URL is filled in.</summary>
public sealed record DiscordSettings
{
    /// <summary>The channel's webhook URL. It works like a password, so it only lives in config.json.</summary>
    public string? WebhookUrl { get; init; }

    public bool Online { get; init; } = true;

    public bool Stopped { get; init; } = true;

    public bool Crashed { get; init; } = true;

    public bool PlayerJoined { get; init; }

    public bool PlayerLeft { get; init; }

    public bool IsOn(DiscordEvent kind) => DiscordWebhook.IsValidUrl(WebhookUrl) && kind switch
    {
        DiscordEvent.Online => Online,
        DiscordEvent.Stopped => Stopped,
        DiscordEvent.Crashed => Crashed,
        DiscordEvent.PlayerJoined => PlayerJoined,
        DiscordEvent.PlayerLeft => PlayerLeft,
        _ => false,
    };
}
