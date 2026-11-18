namespace WaNut.Bridge.Discord;

/// <summary>The message a Discord reply points at: who wrote it and its text, for a short hint in game.</summary>
public sealed record InboundReply(string AuthorName, string Excerpt);
