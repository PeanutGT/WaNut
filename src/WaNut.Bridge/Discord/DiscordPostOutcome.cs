namespace WaNut.Bridge.Discord;

public enum DiscordPostOutcome
{
    Sent,

    /// <summary>429: send the same post again after <see cref="DiscordPostResult.Wait"/>.</summary>
    RateLimited,

    /// <summary>A network error or a Discord 5xx: worth another try.</summary>
    Failed,

    /// <summary>Discord said no for good (bad token, deleted webhook, missing permission): drop the post.</summary>
    Rejected,
}
