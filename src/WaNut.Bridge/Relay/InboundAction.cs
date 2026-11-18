namespace WaNut.Bridge.Relay;

/// <param name="Author">For <see cref="InboundActionKind.Chat"/> and <see cref="InboundActionKind.Console"/>.</param>
/// <param name="Text">The chat text, or the server command without a leading slash.</param>
public sealed record InboundAction(InboundActionKind Kind, string? Author = null, string? Text = null, ulong? UserId = null)
{
    public static InboundAction Ignore { get; } = new(InboundActionKind.Ignore);
}
