using System.Globalization;
using System.Text.RegularExpressions;

namespace WaNut.Bridge.Relay;

/// <summary>The answer to the server's <c>list</c> command.</summary>
public sealed partial record PlayerList(int Online, int Max, IReadOnlyList<string> Names)
{
    /// <summary>
    /// Reads Vanilla's "There are 2 of a max of 20 players online: Steve, Alex" and the Bukkit wording
    /// "There are 2 out of maximum 20 players online." with names on the next lines, possibly grouped as
    /// "default: Steve, Alex". Color codes are ignored.
    /// </summary>
    public static PlayerList? TryParse(string output)
    {
        var text = ColorCode().Replace(output, string.Empty);
        var match = Header().Match(text);
        if (!match.Success)
        {
            return null;
        }

        var names = new List<string>();
        foreach (var part in match.Groups["names"].Value.Split([',', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var name = part[(part.LastIndexOf(": ", StringComparison.Ordinal) + 1)..].Trim();
            if (PlayerName().IsMatch(name))
            {
                names.Add(name);
            }
        }

        return new PlayerList(
            int.Parse(match.Groups["online"].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups["max"].Value, CultureInfo.InvariantCulture),
            names);
    }

    [GeneratedRegex(@"There are (?<online>\d+) (?:of a max of|out of maximum|/) ?(?<max>\d+) players online[.:]?(?<names>.*)", RegexOptions.Singleline)]
    private static partial Regex Header();

    [GeneratedRegex("§.")]
    private static partial Regex ColorCode();

    [GeneratedRegex("^[A-Za-z0-9_]{1,16}$")]
    private static partial Regex PlayerName();
}
