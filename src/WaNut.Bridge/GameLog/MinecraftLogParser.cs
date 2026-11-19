using System.Text.RegularExpressions;
using WaNut.Core.Hosting;

namespace WaNut.Bridge.GameLog;

/// <summary>
/// Reads the events the bridge forwards out of <c>latest.log</c> lines. Covers the line shapes of Vanilla and Fabric
/// ("[12:00:00] [Server thread/INFO]: "), Paper and Purpur ("[12:00:00 INFO]: ") and Forge and NeoForge
/// ("[12:00:00] [Server thread/INFO] [minecraft/MinecraftServer]: "). Only the text after the first "]: " is read,
/// so chat can never pass for a server message. Private messages (/tell, /msg, /w) are never logged as chat, and
/// the "issued server command" lines some servers write for them match nothing here. Minecraft 26.x writes
/// "System chat: " before joins, deaths and advancements, and Paper 26.x before player chat too; it is dropped first.
/// </summary>
public static partial class MinecraftLogParser
{
    /// <param name="isOnline">
    /// Tells whether a name belongs to a player in the game now. Paper 26.x logs chat as "Name: text" without the angle
    /// brackets; without "[Not Secure] " in front, such a line counts as chat only for a player who is online, so a
    /// plugin line like "Timings: ..." is never sent to Discord as chat.
    /// </param>
    public static GameEvent? Parse(string line, Func<string, bool>? isOnline = null)
    {
        line = ServerLogParser.Clean(line);
        if (ServerLogParser.MessageOf(line) is not { } message)
        {
            return null;
        }

        // Chat first: a chat line starts with "<" or a name and ": ", so no later rule can be fooled by what a player types.
        var chat = ChatMessage().Match(message);
        if (chat.Success)
        {
            return new GameEvent(GameEventKind.Chat, chat.Groups["name"].Value, chat.Groups["text"].Value);
        }

        var plainChat = PlainChatMessage().Match(message);
        if (plainChat.Success && (plainChat.Groups["unsigned"].Success || isOnline?.Invoke(plainChat.Groups["name"].Value) == true))
        {
            return new GameEvent(GameEventKind.Chat, plainChat.Groups["name"].Value, plainChat.Groups["text"].Value);
        }

        switch (ServerLogParser.Parse(line))
        {
            case { Kind: LogEventKind.Online }:
                return new GameEvent(GameEventKind.ServerStarted);
            case { Kind: LogEventKind.Stopping }:
                return new GameEvent(GameEventKind.ServerStopping);
            case { Kind: LogEventKind.PlayerJoined, PlayerName: var joined }:
                return new GameEvent(GameEventKind.Joined, joined);
            case { Kind: LogEventKind.PlayerLeft, PlayerName: var left }:
                return new GameEvent(GameEventKind.Left, left);
        }

        var advancement = AdvancementMessage().Match(message);
        if (advancement.Success)
        {
            return new GameEvent(GameEventKind.Advancement, advancement.Groups["name"].Value, message);
        }

        var death = DeathMessage().Match(message);
        return death.Success ? new GameEvent(GameEventKind.Death, death.Groups["name"].Value, message) : null;
    }

    // 1.19+ marks unsigned chat with "[Not Secure] ".
    [GeneratedRegex(@"^(?:\[Not Secure\] )?<(?<name>[A-Za-z0-9_]{1,16})> (?<text>.+)$")]
    private static partial Regex ChatMessage();

    // Paper 26.x: "[Not Secure] Steve: hello" or "Steve: hello".
    [GeneratedRegex(@"^(?<unsigned>\[Not Secure\] )?(?<name>[A-Za-z0-9_]{1,16}): (?<text>.+)$")]
    private static partial Regex PlainChatMessage();

    [GeneratedRegex(@"^(?<name>[A-Za-z0-9_]{1,16}) has (?:made the advancement|completed the challenge|reached the goal) \[.+\]$")]
    private static partial Regex AdvancementMessage();

    // The openings of every vanilla death message (en_us death.* keys, 1.16-1.21). A tamed pet's death is logged
    // as "Named entity ... died: ...", which does not start with a name and a death verb.
    [GeneratedRegex(@"^(?<name>[A-Za-z0-9_]{1,16}) (?:was |were |walked into |drowned|died|experienced kinetic energy|blew up|hit the ground too hard|fell |went up in flames|went off with a bang|burned to death|tried to swim in lava|suffocated in a wall|starved to death|froze to death|withered away|discovered the floor was lava|didn't want to live|left the confines of this world|got finished off|squashed )")]
    private static partial Regex DeathMessage();
}
