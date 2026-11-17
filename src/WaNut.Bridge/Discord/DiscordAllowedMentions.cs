namespace WaNut.Bridge.Discord;

public sealed record DiscordAllowedMentions
{
    public IReadOnlyList<string> Parse { get; init; } = [];
}
