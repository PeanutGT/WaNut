using System.Globalization;
using System.Text.RegularExpressions;
using WaNut.Core.Hosting;

namespace WaNut.Core.Hotplug.Safety;

/// <summary>
/// Asks the console how busy the server is: <c>list</c> for players, then the first of <c>mspt</c> (Paper, Purpur),
/// <c>tick query</c> (vanilla and Fabric 1.20.3+) and <c>tps</c> (Spigot) that the server knows. A server that knows none
/// answers "Unknown command" at once, so trying them costs little.
/// </summary>
public static partial class ServerLoadProbe
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(3);

    /// <summary>Below this TPS the server is falling behind; at 20 TPS "tps" can't tell how much budget is left.</summary>
    private const double BehindTps = 19.0;

    public static async Task<ServerLoad> ReadAsync(IServerConsole console, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var wait = timeout ?? DefaultTimeout;
        var list = await ConsoleQuery.AskAsync(console, "list", l => Players(l) is not null, wait, cancellationToken).ConfigureAwait(false);
        var players = list is null ? null : Players(list);

        foreach (var (command, read) in Readers)
        {
            var answer = await ConsoleQuery.AskAsync(console, command, l => read(l) is not null || IsUnknownCommand(l), wait, cancellationToken).ConfigureAwait(false);
            if (answer is not null && read(answer) is { } value)
            {
                return new ServerLoad(players, value.Mspt, command);
            }
        }

        return new ServerLoad(players, null);
    }

    private static readonly (string Command, Func<string, Reading?> Read)[] Readers =
    [
        ("mspt", l => FromMspt(l) is { } v ? new Reading(v) : null),
        ("tick query", l => FromTickQuery(l) is { } v ? new Reading(v) : null),
        ("tps", l => FromTps(l) is { } tps ? new Reading(tps < BehindTps ? 1000 / tps : null) : null),
    ];

    /// <summary>"There are 3 of a max of 20 players online: a, b, c" (vanilla), "There are 3 out of maximum 20 players online." (Paper), "There are 3/20 players online:" (Bukkit).</summary>
    public static int? Players(string line)
    {
        var message = ReloadLogPatterns.Message(line);
        var match = ListAnswer().Match(message);
        return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }

    /// <summary>Paper's second line, "◴ 3.1/1.2/10.5, 3.0/1.1/12.0, 2.9/1.0/15.3": the 5-second average.</summary>
    public static double? FromMspt(string line)
    {
        var match = MsptAnswer().Match(ReloadLogPatterns.Message(line));
        return match.Success ? Number(match.Groups[1].Value) : null;
    }

    /// <summary>"Average time per tick: 12.3ms (Target: 50.0ms)".</summary>
    public static double? FromTickQuery(string line)
    {
        var match = TickQueryAnswer().Match(ReloadLogPatterns.Message(line));
        return match.Success ? Number(match.Groups[1].Value) : null;
    }

    /// <summary>"TPS from last 1m, 5m, 15m: 19.5, 20.0, 20.0" (Paper adds "*" above 20): the 1-minute value.</summary>
    public static double? FromTps(string line)
    {
        var match = TpsAnswer().Match(ReloadLogPatterns.Message(line));
        return match.Success ? Number(match.Groups[1].Value) : null;
    }

    public static bool IsUnknownCommand(string line) =>
        UnknownCommand().IsMatch(ReloadLogPatterns.Message(line));

    /// <summary>An answer; at full speed "tps" answers without telling the tick time.</summary>
    private sealed record Reading(double? Mspt);

    private static double Number(string text) => double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

    // Anchored at the start of the message, so a chat line ("<Steve> There are 0 ...") never counts.
    [GeneratedRegex(@"^There (?:are|is) (\d+)\b[^<]*?players? online", RegexOptions.CultureInvariant)]
    private static partial Regex ListAnswer();

    [GeneratedRegex(@"^\W*(\d+(?:\.\d+)?)/\d+(?:\.\d+)?/\d+(?:\.\d+)?,", RegexOptions.CultureInvariant)]
    private static partial Regex MsptAnswer();

    [GeneratedRegex(@"^Average time per tick: (\d+(?:\.\d+)?)\s*ms", RegexOptions.CultureInvariant)]
    private static partial Regex TickQueryAnswer();

    [GeneratedRegex(@"^TPS from last [^:]*:\s*\*?(\d+(?:\.\d+)?)", RegexOptions.CultureInvariant)]
    private static partial Regex TpsAnswer();

    [GeneratedRegex(@"^Unknown (?:or incomplete )?command", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex UnknownCommand();
}
