using System.Text.RegularExpressions;

namespace WaNut.Core.Hotplug.Safety;

/// <summary>
/// Tells a console line that says a data pack failed to load from everything else. The wording comes from Minecraft's
/// own log messages (1.16 to 1.21: "Couldn't parse data file", "Couldn't parse element", "Couldn't parse loot table",
/// "Failed to load function", ...). Only WARN, ERROR or FATAL lines count, so a player typing the same words in chat
/// or with /say can't trigger a rollback; the one exception is the /reload answer "Reload failed; keeping old data",
/// which Minecraft prints at INFO.
/// </summary>
public static partial class ReloadLogPatterns
{
    /// <summary>The phrases that mark a failed load, matched without regard to case.</summary>
    public static readonly IReadOnlyList<string> Phrases =
    [
        "Failed to load datapacks",
        "Missing metadata in pack",
        "Couldn't parse data file",
        "Unknown registry key",
        // Also covers "Couldn't parse element" (1.20+) and "Couldn't parse loot table" (1.16-1.19).
        "Couldn't parse",
        "Failed to load function",
        "Parsing error loading recipe",
        "Parsing error loading custom advancement",
        "Couldn't load tag",
        "Couldn't read tag list",
        "Failed to execute reload",
    ];

    private const string ReloadFailed = "Reload failed";

    /// <summary>True when <paramref name="line"/> reports a data pack that failed to load.</summary>
    public static bool IsFailure(string line)
    {
        var (level, message) = Split(line);
        if (message is null)
        {
            return false;
        }

        if (message.StartsWith(ReloadFailed, StringComparison.OrdinalIgnoreCase))
        {
            // The /reload answer itself; a chat or /say line starts with "<name>" or "[name]" instead.
            return true;
        }

        return level is "WARN" or "WARNING" or "ERROR" or "FATAL" or "SEVERE"
            && Phrases.Any(p => message.Contains(p, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>True for a line that has no log prefix of its own: a stack trace or the rest of a multi-line message.</summary>
    public static bool IsContinuation(string line) => !Prefix().IsMatch(line);

    /// <summary>The message without its log prefix, or the whole line when it has none.</summary>
    public static string Message(string line) => Split(line).Message ?? line.Trim();

    /// <summary>
    /// The level and message of a log line: "[12:00:00] [Server thread/ERROR]: msg" (vanilla, Fabric),
    /// "[12:00:00 ERROR]: msg" (Paper), "[19Nov2026 12:00:00.000] [Server thread/ERROR] [minecraft/LootDataManager]: msg"
    /// (Forge). Null message for a line without a prefix.
    /// </summary>
    private static (string? Level, string? Message) Split(string line)
    {
        var match = Prefix().Match(line);
        return match.Success ? (match.Groups["level"].Value.ToUpperInvariant(), line[match.Length..].Trim()) : (null, null);
    }

    // A time in brackets, then optionally "[thread/" , the level, "]", any further bracketed logger names, then ":" or space.
    [GeneratedRegex(@"^\s*\[[^\]]*\d{1,2}:\d{2}:\d{2}(?:\.\d+)?(?:\s+(?<level>[A-Za-z]+))?\]\s*(?:\[[^\]]*?/(?<level>[A-Za-z]+)\])?(?:\s*\[[^\]]*\])*\s*:?\s?", RegexOptions.CultureInvariant)]
    private static partial Regex Prefix();
}
