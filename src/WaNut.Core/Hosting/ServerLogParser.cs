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

        if (message == "Saved the game")
        {
            return new LogEvent(LogEventKind.Saved);
        }

        var match = PlayerMessage().Match(message);
        if (!match.Success)
        {
            return null;
        }

        var kind = match.Groups["action"].Value == "joined" ? LogEventKind.PlayerJoined : LogEventKind.PlayerLeft;
        return new LogEvent(kind, match.Groups["name"].Value);
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

    // Java Edition names are 3-16 letters, digits or underscores; offline-mode names can be shorter.
    [GeneratedRegex(@"^(?<name>[A-Za-z0-9_]{1,16}) (?<action>joined|left) the game$")]
    private static partial Regex PlayerMessage();
}
