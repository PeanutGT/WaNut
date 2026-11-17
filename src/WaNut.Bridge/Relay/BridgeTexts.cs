using System.Globalization;

namespace WaNut.Bridge.Relay;

/// <summary>The bridge's own words in Discord and in the bot status.</summary>
public static class BridgeTexts
{
    public const string ServerStarted = "✅ Server started";

    public const string ServerStopping = "🛑 Server stopping";

    public const string ServerOffline = "The server is offline.";

    public const string PresenceOffline = "Server offline";

    public const string PresenceOnline = "Server online";

    public const string ConsoleDenied = "Only members with the admin role can use /cmd.";

    public const string ConsoleNoOutput = "(no output)";

    public static string Joined(string player) => $"{player} joined the game";

    public static string Left(string player) => $"{player} left the game";

    public static string Online(PlayerList list) =>
        list.Names.Count == 0
            ? string.Format(CultureInfo.InvariantCulture, "Nobody is online (0/{0}).", list.Max)
            : string.Format(CultureInfo.InvariantCulture, "Online ({0}/{1}): {2}", list.Online, list.Max, string.Join(", ", list.Names));

    public static string Presence(PlayerList list) => string.Format(CultureInfo.InvariantCulture, "{0} / {1} online", list.Online, list.Max);
}
