namespace WaNut.Bridge.Discord;

/// <summary>The body of one Discord "create message" or "execute webhook" request.</summary>
public sealed record DiscordPost
{
    public string? Content { get; init; }

    public string? Username { get; init; }

    public string? AvatarUrl { get; init; }

    public IReadOnlyList<DiscordEmbed>? Embeds { get; init; }

    /// <summary>Always empty, so "@everyone" or a role mention typed in game pings nobody.</summary>
    public DiscordAllowedMentions AllowedMentions { get; init; } = new();
}
