namespace WaNut.Bridge.Discord;

/// <summary>A slash command someone used, with what is needed to answer it.</summary>
/// <param name="InteractionId">With <paramref name="Token"/>, answers this use of the command (valid 15 minutes).</param>
/// <param name="Options">Option values by name, as text.</param>
public sealed record SlashCommand(
    string InteractionId,
    string Token,
    string ApplicationId,
    ulong ChannelId,
    ulong UserId,
    string UserName,
    IReadOnlyList<ulong> RoleIds,
    string Name,
    IReadOnlyDictionary<string, string> Options);
