namespace WaNut.Bridge.Relay;

/// <param name="Author">For <see cref="InboundActionKind.Chat"/> and <see cref="InboundActionKind.Console"/>.</param>
/// <param name="Text">The chat text, or the server command without a leading slash.</param>
public sealed record InboundAction(InboundActionKind Kind, string? Author = null, string? Text = null, ulong? UserId = null)
{
    /// <summary>Files that came with a chat message.</summary>
    public IReadOnlyList<WaNut.Bridge.Discord.InboundAttachment> Attachments { get; init; } = [];

    /// <summary>The message a chat message replies to.</summary>
    public WaNut.Bridge.Discord.InboundReply? Reply { get; init; }

    /// <summary>The channel a /status was used in.</summary>
    public ulong ChannelId { get; init; }

    public static InboundAction Ignore { get; } = new(InboundActionKind.Ignore);
}
