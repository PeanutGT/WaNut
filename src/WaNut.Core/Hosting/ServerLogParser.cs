using System.Text.RegularExpressions;

namespace WaNut.Core.Hosting;

public enum LogEventKind
{
    Online,
    Stopping,
    PlayerJoined,
    PlayerLeft,

    /// <summary>The world was written to disk, as after "save-all".</summary>
    Saved,
}

/// <summary>Where a starting or stopping server is, for the loading steps. Not every server type prints every line.</summary>
public enum ServerPhase
{
    /// <summary>"Preparing level", "Preparing start region", "Preparing spawn area: 40%".</summary>
    LoadingWorld,

    /// <summary>"Saving worlds", "Saving chunks for level ...".</summary>
    SavingWorld,

    /// <summary>"All dimensions are saved", "All chunks are saved": only closing is left.</summary>
    WorldSaved,
}

public sealed record LogEvent(LogEventKind Kind, string? PlayerName = null);

public enum LogLineLevel
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// Reads server state out of console lines. Works for Vanilla and Fabric ("[12:00:00] [Server thread/INFO]: ...")
/// and Paper ("[12:00:00 INFO]: ...").
/// </summary>
public static partial class ServerLogParser
{
    private const string MessageMarker = "]: ";

    private const string SystemChatPrefix = "System chat: ";

    /// <summary>Removes ANSI color codes and Minecraft formatting codes (§.), which some servers print even when the output is not a terminal.</summary>
    public static string Clean(string line) => SectionEscape().Replace(AnsiEscape().Replace(line, string.Empty), string.Empty);

    /// <summary>
    /// The message part of a log line: the text after the first "]: ", without the "System chat: " that Minecraft 26.x
    /// puts before joins, deaths, advancements and, on Paper, player chat. Null when the line has no "]: ".
    /// </summary>
    public static string? MessageOf(string line)
    {
        var start = line.IndexOf(MessageMarker, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        var message = line[(start + MessageMarker.Length)..].TrimEnd();
        return message.StartsWith(SystemChatPrefix, StringComparison.Ordinal) ? message[SystemChatPrefix.Length..] : message;
    }

    public static LogEvent? Parse(string line)
    {
        // Only the text after the first "]: " is the message. Anything later in the line, such as chat,
        // can never pass for a server message: a player typing "]: Steve joined the game" does not add Steve.
        if (MessageOf(Clean(line)) is not { } message)
        {
            return null;
        }

        if (DoneMessage().IsMatch(message))
        {
            return new LogEvent(LogEventKind.Online);
        }

        if (message == "Stopping server")
        {
            return new LogEvent(LogEventKind.Stopping);
        }

        if (message == "Saved the game")
        {
            return new LogEvent(LogEventKind.Saved);
        }

        var match = PlayerMessage().Match(message);
        if (match.Success)
        {
            var action = match.Groups["action"].Value;
            var kind = action is "joined" or "加入了" ? LogEventKind.PlayerJoined : LogEventKind.PlayerLeft;
            return new LogEvent(kind, match.Groups["name"].Value);
        }

        var loginMatch = PlayerLoginMessage().Match(message);
        if (loginMatch.Success)
        {
            return new LogEvent(LogEventKind.PlayerJoined, loginMatch.Groups["name"].Value);
        }

        var disconnectMatch = PlayerDisconnectMessage().Match(message);
        if (disconnectMatch.Success)
        {
            return new LogEvent(LogEventKind.PlayerLeft, disconnectMatch.Groups["name"].Value);
        }

        return null;
    }

    /// <summary>The start or stop phase a console line shows, or null for any other line.</summary>
    public static ServerPhase? Phase(string line)
    {
        var start = line.IndexOf(MessageMarker, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        var message = line[(start + MessageMarker.Length)..].TrimEnd();
        if (message.StartsWith("Preparing level", StringComparison.Ordinal)
            || message.StartsWith("Preparing start region", StringComparison.Ordinal)
            || message.StartsWith("Preparing spawn area", StringComparison.Ordinal))
        {
            return ServerPhase.LoadingWorld;
        }

        if (message == "Saving worlds" || message.StartsWith("Saving chunks for level", StringComparison.Ordinal))
        {
            return ServerPhase.SavingWorld;
        }

        if (message.EndsWith("All dimensions are saved", StringComparison.Ordinal) || message.EndsWith("All chunks are saved", StringComparison.Ordinal))
        {
            return ServerPhase.WorldSaved;
        }

        return null;
    }

    /// <summary>The level in a line's prefix ("Server thread/WARN" or "12:00:00 ERROR"). Lines without one count as Info.</summary>
    public static LogLineLevel GetLevel(string line)
    {
        var end = line.IndexOf(MessageMarker, StringComparison.Ordinal);
        if (end < 0)
        {
            return LogLineLevel.Info;
        }

        return LevelPrefix().Match(line[..(end + 1)]).Groups["level"].Value switch
        {
            "WARN" => LogLineLevel.Warning,
            "ERROR" or "FATAL" => LogLineLevel.Error,
            _ => LogLineLevel.Info,
        };
    }

    [GeneratedRegex(@"[/ ](?<level>[A-Z]+)\]$")]
    private static partial Regex LevelPrefix();

    [GeneratedRegex(@"\x1B\[[0-9;?]*[A-Za-z]")]
    private static partial Regex AnsiEscape();

    [GeneratedRegex(@"^Done \([\d.,]+s\)! For help, type ""help""")]
    private static partial Regex DoneMessage();

    [GeneratedRegex(@"§.")]
    private static partial Regex SectionEscape();

    // Java Edition names are 3-16 letters, digits or underscores; offline-mode names can be shorter;
    // Bedrock players via Geyser/Floodgate may have prefixes like "." or "*".
    // Covers standard messages, localized join/leave, and optional trailing punctuation.
    [GeneratedRegex(@"^(?<name>[A-Za-z0-9_.+*][A-Za-z0-9_.+* -]{0,31}) (?:(?<action>joined|left) the game|(?<action>加入了|離開了)(?:遊戲|伺服器))[.!~]?$")]
    private static partial Regex PlayerMessage();

    // Fallback when a server or plugin suppresses the public "joined the game" announcement,
    // e.g. "Steve[/127.0.0.1:12345] logged in with entity id 123 at ([world]0.0, 64.0, 0.0)".
    [GeneratedRegex(@"^(?<name>[A-Za-z0-9_.+*][A-Za-z0-9_.+* -]{0,31})\[(?:/[0-9a-fA-F.:]+|local.*)\] logged in with entity id")]
    private static partial Regex PlayerLoginMessage();

    // Covers disconnects, timeouts and kicks (e.g. "Steve lost connection: Disconnected", "Steve lost connection: Timed out").
    [GeneratedRegex(@"^(?<name>[A-Za-z0-9_.+*][A-Za-z0-9_.+* -]{0,31}) lost connection:")]
    private static partial Regex PlayerDisconnectMessage();
}
