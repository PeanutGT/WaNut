namespace WaNut.Core.Bridge;

/// <summary>One channel permission overwrite: for a role, or for one member when <paramref name="IsMember"/>.</summary>
public sealed record DiscordOverwrite(string Id, bool IsMember, long Allow, long Deny);
