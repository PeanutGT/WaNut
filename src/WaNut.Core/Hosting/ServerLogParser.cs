using System.Text.RegularExpressions;

namespace WaNut.Core.Hosting;

public enum LogEventKind
{
    Online,
    Stopping,
    PlayerJoined,
    PlayerLeft,
}

public sealed record LogEvent(LogEventKind Kind, string? PlayerName = null);

/// <summary>
/// Reads server state out of console lines. Works for Vanilla and Fabric ("[12:00:00] [Server thread/INFO]: ...")
/// and Paper ("[12:00:00 INFO]: ...").
/// </summary>
public static partial class ServerLogParser
{
    private const string MessageMarker = "]: ";

    /// <summary>Removes ANSI color codes, which some servers print even when the output is not a terminal.</summary>
    public static string Clean(string line) => AnsiEscape().Replace(line, string.Empty);

    public static LogEvent? Parse(string line)
    {
        // Only the text after the first "]: " is the message. Anything later in the line, such as chat,
        // can never pass for a server message: a player typing "]: Steve joined the game" does not add Steve.
        var start = line.IndexOf(MessageMarker, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        var message = line[(start + MessageMarker.Length)..].TrimEnd();
        if (DoneMessage().IsMatch(message))
        {
            return new LogEvent(LogEventKind.Online);
        }

        if (message == "Stopping server")
        {
            return new LogEvent(LogEventKind.Stopping);
        }

        var match = PlayerMessage().Match(message);
        if (!match.Success)
        {
            return null;
        }

        var kind = match.Groups["action"].Value == "joined" ? LogEventKind.PlayerJoined : LogEventKind.PlayerLeft;
        return new LogEvent(kind, match.Groups["name"].Value);
    }

    [GeneratedRegex(@"\x1B\[[0-9;?]*[A-Za-z]")]
    private static partial Regex AnsiEscape();

    [GeneratedRegex(@"^Done \([\d.,]+s\)! For help, type ""help""")]
    private static partial Regex DoneMessage();

    // Java Edition names are 3-16 letters, digits or underscores; offline-mode names can be shorter.
    [GeneratedRegex(@"^(?<name>[A-Za-z0-9_]{1,16}) (?<action>joined|left) the game$")]
    private static partial Regex PlayerMessage();
}
