namespace WaNut.Core.Settings;

/// <summary>
/// Reads and sets plain values in a block-style YAML file, such as the ones Paper writes, by a dotted path like
/// "collisions.max-entity-collisions". Keeps every other line, comment and the order as they are. Lists and
/// flow style ("{ a: 1 }") are not understood, which Paper's settings that WaNut changes never use.
/// </summary>
public static class YamlScalars
{
    private const int Indent = 2;

    public static string? Get(string text, string path)
    {
        var lines = SplitLines(text);
        return Find(lines, path.Split('.')) is { Found: true } hit ? Unquote(ValueOf(lines[hit.Line])) : null;
    }

    /// <summary>Returns the text with the value at <paramref name="path"/> replaced, or added with any missing parents.</summary>
    public static string Set(string text, string path, string value)
    {
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = SplitLines(text);
        var keys = path.Split('.');
        var hit = Find(lines, keys);
        if (hit.Found)
        {
            var line = lines[hit.Line];
            var colon = KeyEnd(line);
            lines[hit.Line] = line[..(colon + 1)] + " " + value + Comment(line[(colon + 1)..]);
        }
        else
        {
            // The missing keys go after the last line of the deepest parent that is there.
            var insertAt = hit.Line < 0 ? TrimEnd(lines) : BlockEnd(lines, hit.Line);
            var added = new List<string>();
            for (var depth = hit.Depth; depth < keys.Length; depth++)
            {
                var pad = new string(' ', depth * Indent);
                added.Add(depth == keys.Length - 1 ? $"{pad}{keys[depth]}: {value}" : $"{pad}{keys[depth]}:");
            }

            lines.InsertRange(insertAt, added);
        }

        return string.Join(newline, lines);
    }

    private readonly record struct Hit(bool Found, int Line, int Depth);

    /// <summary>The line of the whole path, or else the line and depth of the deepest parent found (-1, 0 for none).</summary>
    private static Hit Find(List<string> lines, string[] keys)
    {
        var parentLine = -1;
        var depth = 0;
        var parentIndent = -1;
        var expected = ExpectedIndent(lines, parentLine, parentIndent);
        for (var i = 0; i < lines.Count; i++)
        {
            if (KeyOf(lines[i]) is not { } key)
            {
                continue;
            }

            var indent = IndentOf(lines[i]);
            if (indent <= parentIndent)
            {
                break; // Left the parent's block without finding the next key.
            }

            if (indent == expected && key == keys[depth])
            {
                if (depth == keys.Length - 1)
                {
                    return new Hit(true, i, depth);
                }

                parentLine = i;
                parentIndent = indent;
                expected = ExpectedIndent(lines, parentLine, parentIndent);
                depth++;
            }
        }

        return new Hit(false, parentLine, depth);
    }

    // Children sit at the indent of the first key under the parent, which is 0 at the top.
    private static int ExpectedIndent(List<string> lines, int parentLine, int parentIndent)
    {
        for (var i = parentLine + 1; i < lines.Count; i++)
        {
            if (KeyOf(lines[i]) is not null)
            {
                var indent = IndentOf(lines[i]);
                return indent > parentIndent ? indent : -1;
            }
        }

        return -1;
    }

    private static int BlockEnd(List<string> lines, int parentLine)
    {
        var indent = IndentOf(lines[parentLine]);
        var end = parentLine + 1;
        for (var i = parentLine + 1; i < lines.Count; i++)
        {
            if (lines[i].Trim().Length == 0 || lines[i].TrimStart().StartsWith('#'))
            {
                continue;
            }

            if (IndentOf(lines[i]) <= indent)
            {
                break;
            }

            end = i + 1;
        }

        return end;
    }

    private static int TrimEnd(List<string> lines)
    {
        var end = lines.Count;
        while (end > 0 && lines[end - 1].Trim().Length == 0)
        {
            end--;
        }

        return end;
    }

    private static List<string> SplitLines(string text) => [.. text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')];

    private static string? KeyOf(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] is '#' or '-')
        {
            return null;
        }

        var colon = KeyEnd(line);
        return colon < 0 ? null : Unquote(line[..colon].Trim());
    }

    // The colon that ends the key: one followed by a space or the end of the line, outside quotes.
    private static int KeyEnd(string line)
    {
        var quote = '\0';
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quote != '\0')
            {
                quote = c == quote ? '\0' : quote;
            }
            else if (c is '\'' or '"')
            {
                quote = c;
            }
            else if (c == ':' && (i + 1 == line.Length || line[i + 1] == ' '))
            {
                return i;
            }
        }

        return -1;
    }

    private static int IndentOf(string line) => line.Length - line.TrimStart(' ').Length;

    private static string ValueOf(string line)
    {
        var rest = line[(KeyEnd(line) + 1)..];
        var comment = Comment(rest);
        return rest[..(rest.Length - comment.Length)].Trim();
    }

    // A trailing " # comment" after an unquoted value, kept when the value changes.
    private static string Comment(string rest)
    {
        var trimmed = rest.TrimStart();
        if (trimmed.StartsWith('\'') || trimmed.StartsWith('"'))
        {
            return string.Empty;
        }

        var hash = rest.IndexOf(" #", StringComparison.Ordinal);
        return hash < 0 ? string.Empty : rest[hash..];
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && value[0] is '\'' or '"' && value[^1] == value[0] ? value[1..^1] : value;
}
