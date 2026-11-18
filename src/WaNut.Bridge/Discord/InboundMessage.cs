namespace WaNut.Bridge.Discord;

/// <summary>A Discord message as the bridge needs it, away from the Discord library's types.</summary>
/// <param name="AuthorName">The server nickname, else the display name, else the username.</param>
/// <param name="IsWebhook">Posted by a webhook, which includes the bridge's own posts.</param>
/// <param name="Content">Mentions already turned into readable "@name" text.</param>
public sealed record InboundMessage(
    ulong ChannelId,
    ulong AuthorId,
    string AuthorName,
    bool IsBot,
    bool IsWebhook,
    IReadOnlyList<ulong> RoleIds,
    string Content,
    IReadOnlyList<InboundAttachment>? Attachments = null)
{
    public IReadOnlyList<InboundAttachment> Files => Attachments ?? [];
}
