namespace WaNut.Core.Hotplug;

/// <summary>
/// Reads the console's answer to <c>datapack list</c>: "There are 2 data pack(s) enabled: [vanilla (built-in)],
/// [file/loot.zip (world)]" or "There are no data packs enabled". The wording has moved a little between versions.
/// </summary>
public static class DatapackListParser
{
    /// <summary>True when <paramref name="line"/> answers "datapack list enabled" (or "available" when <paramref name="enabled"/> is false).</summary>
    public static bool IsAnswer(string line, bool enabled = true)
    {
        var at = line.IndexOf("There ", StringComparison.Ordinal);
        if (at < 0 || line.Contains('<', StringComparison.Ordinal))
        {
            // A chat line ("<Steve> There are ...") is a player talking, not the answer.
            return false;
        }

        var rest = line[at..];
        return rest.Contains("data pack", StringComparison.Ordinal)
            && rest.Contains(enabled ? "enabled" : "available", StringComparison.Ordinal);
    }

    /// <summary>The pack ids named in an answer line, such as "file/loot.zip" and "vanilla", without their source note.</summary>
    public static IReadOnlyList<string> Packs(string line)
    {
        var colon = line.IndexOf(':', Math.Max(0, line.IndexOf("There ", StringComparison.Ordinal)));
        if (colon < 0)
        {
            return [];
        }

        var packs = new List<string>();
        var text = line[(colon + 1)..];
        var i = 0;
        while ((i = text.IndexOf('[', i)) >= 0)
        {
            // A pack name may hold parentheses ("pack (1).zip"), so the closing bracket is found by depth.
            var depth = 0;
            var end = -1;
            for (var j = i; j < text.Length; j++)
            {
                if (text[j] == '[')
                {
                    depth++;
                }
                else if (text[j] == ']' && --depth == 0)
                {
                    end = j;
                    break;
                }
            }

            if (end < 0)
            {
                break;
            }

            packs.Add(StripSource(text[(i + 1)..end]));
            i = end + 1;
        }

        return packs;
    }

    /// <summary>Whether the answer line lists <paramref name="fileName"/> from the world's datapacks folder.</summary>
    public static bool Lists(string line, string fileName) =>
        Packs(line).Contains(HotplugNames.PackId(fileName), StringComparer.Ordinal);

    /// <summary>"file/loot.zip (world)" becomes "file/loot.zip"; "file/pack (1).zip" stays as it is.</summary>
    private static string StripSource(string entry)
    {
        if (entry.EndsWith(')'))
        {
            var open = entry.LastIndexOf(" (", StringComparison.Ordinal);
            if (open > 0 && !entry[(open + 2)..^1].Contains('('))
            {
                var source = entry[(open + 2)..^1];
                if (source is "world" or "built-in" or "feature" or "server" or "mod resources" || !source.Contains('.'))
                {
                    return entry[..open];
                }
            }
        }

        return entry;
    }
}
