using System.Globalization;
using System.Text.RegularExpressions;

namespace WaNut.Bridge.Relay;

/// <summary>How fast the server ticks. Either number may be missing; <see cref="Tps"/> never exceeds 20.</summary>
public sealed partial record TickStats(double? Tps, double? Mspt)
{
    /// <summary>Paper, Spigot, Purpur: "TPS from last 1m, 5m, 15m: *20.0, 20.0, 20.0". The first number is the newest.</summary>
    public static TickStats? TryParseTps(string output)
    {
        var match = PaperTps().Match(output);
        return match.Success && double.TryParse(match.Groups["tps"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var tps)
            ? new TickStats(Math.Min(20, tps), null)
            : null;
    }

    /// <summary>Vanilla 1.20.3+, <c>tick query</c>: "Average time per tick: 2.5ms (Target: 50.0ms)".</summary>
    public static TickStats? TryParseTickQuery(string output)
    {
        var match = VanillaTick().Match(output);
        if (!match.Success || !double.TryParse(match.Groups["ms"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var mspt) || mspt <= 0)
        {
            return null;
        }

        return new TickStats(Math.Min(20, 1000 / mspt), mspt);
    }

    // Colour codes are stripped by the caller; Paper marks a value that is over 20 with "*".
    [GeneratedRegex(@"TPS from last[^:]*:\s*[*>]?(?<tps>\d+(?:\.\d+)?)")]
    private static partial Regex PaperTps();

    [GeneratedRegex(@"Average time per tick:\s*(?<ms>\d+(?:\.\d+)?)\s*ms")]
    private static partial Regex VanillaTick();
}
