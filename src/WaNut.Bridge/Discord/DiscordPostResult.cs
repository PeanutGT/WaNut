namespace WaNut.Bridge.Discord;

/// <param name="Wait">After <see cref="DiscordPostOutcome.RateLimited"/>, how long to wait; after
/// <see cref="DiscordPostOutcome.Sent"/>, how long until the rate-limit bucket refills (zero when it is not empty).</param>
/// <param name="StatusCode">The HTTP status, for the log.</param>
public sealed record DiscordPostResult(DiscordPostOutcome Outcome, TimeSpan Wait = default, int? StatusCode = null);
