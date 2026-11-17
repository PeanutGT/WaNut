namespace WaNut.Bridge.Discord;

/// <summary>One line for the Discord channel, already escaped. Several are merged into one post when they can be.</summary>
/// <param name="Username">Webhook only: post under this name instead of the webhook's own.</param>
/// <param name="AvatarUrl">Webhook only: the picture next to <paramref name="Username"/>.</param>
/// <param name="EmbedColor">Post as an embed card with this side color instead of plain content.</param>
public sealed record OutgoingMessage(string Text, string? Username = null, string? AvatarUrl = null, int? EmbedColor = null);
