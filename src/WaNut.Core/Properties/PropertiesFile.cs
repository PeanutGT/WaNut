using System.Globalization;
using System.Text;
using WaNut.Core.Instances;

namespace WaNut.Core.Properties;

/// <summary>
/// A Java .properties file such as server.properties. Changing a value rewrites only that line, so comments,
/// unknown keys and their order are kept exactly as the server wrote them.
/// </summary>
public sealed class PropertiesFile
{
    private readonly List<string> _lines;
    private readonly string _newLine;

    private PropertiesFile(List<string> lines, string newLine)
    {
        _lines = lines;
        _newLine = newLine;
    }

    public static PropertiesFile Parse(string text)
    {
        var newLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return new PropertiesFile(lines, newLine);
    }

    /// <summary>Reads the file, or starts an empty one when it does not exist yet.</summary>
    public static PropertiesFile Load(string path) =>
        File.Exists(path) ? Parse(File.ReadAllText(path)) : new PropertiesFile([], Environment.NewLine);

    public IReadOnlyList<string> Keys => Entries().Select(e => e.Key).Distinct().ToList();

    /// <summary>The value for <paramref name="key"/>, or null when the file has no such key. Like Java, the last one wins.</summary>
    public string? Get(string key) => Entries().LastOrDefault(e => e.Key == key)?.Value;

    public void Set(string key, string value)
    {
        var text = Escape(key, isKey: true) + "=" + Escape(value, isKey: false);
        var existing = Entries().LastOrDefault(e => e.Key == key);
        if (existing is null)
        {
            _lines.Add(text);
            return;
        }

        _lines.RemoveRange(existing.FirstLine, existing.LineCount);
        _lines.Insert(existing.FirstLine, text);
    }

    public string ToText()
    {
        var builder = new StringBuilder();
        foreach (var line in _lines)
        {
            builder.Append(line).Append(_newLine);
        }

        return builder.ToString();
    }

    public void Save(string path) => AtomicFile.WriteAllText(path, ToText());

    private sealed record Entry(string Key, string Value, int FirstLine, int LineCount);

    private IEnumerable<Entry> Entries()
    {
        for (var i = 0; i < _lines.Count; i++)
        {
            var first = i;
            var logical = _lines[i].TrimStart(' ', '\t', '\f');
            if (logical.Length == 0 || logical[0] is '#' or '!')
            {
                continue;
            }

            // A line ending in an odd number of backslashes continues on the next line.
            while (EndsWithContinuation(logical) && i + 1 < _lines.Count)
            {
                i++;
                logical = logical[..^1] + _lines[i].TrimStart(' ', '\t', '\f');
            }

            if (EndsWithContinuation(logical))
            {
                logical = logical[..^1];
            }

            var (key, value) = SplitKeyValue(logical);
            yield return new Entry(Unescape(key), Unescape(value), first, i - first + 1);
        }
    }

    private static bool EndsWithContinuation(string line)
    {
        var count = 0;
        for (var i = line.Length - 1; i >= 0 && line[i] == '\\'; i--)
        {
            count++;
        }

        return count % 2 == 1;
    }

    private static (string Key, string Value) SplitKeyValue(string line)
    {
        var end = 0;
        while (end < line.Length)
        {
            var c = line[end];
            if (c == '\\')
            {
                end += 2;
                continue;
            }

            if (c is '=' or ':' or ' ' or '\t' or '\f')
            {
                break;
            }

            end++;
        }

        end = Math.Min(end, line.Length);
        var rest = end;
        while (rest < line.Length && line[rest] is ' ' or '\t' or '\f')
        {
            rest++;
        }

        if (rest < line.Length && line[rest] is '=' or ':')
        {
            rest++;
            while (rest < line.Length && line[rest] is ' ' or '\t' or '\f')
            {
                rest++;
            }
        }

        return (line[..end], line[rest..]);
    }

    private static string Unescape(string text)
    {
        if (!text.Contains('\\', StringComparison.Ordinal))
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c != '\\' || i + 1 >= text.Length)
            {
                builder.Append(c);
                continue;
            }

            var next = text[++i];
            switch (next)
            {
                case 't': builder.Append('\t'); break;
                case 'n': builder.Append('\n'); break;
                case 'r': builder.Append('\r'); break;
                case 'f': builder.Append('\f'); break;
                case 'u' when i + 4 < text.Length
                    && int.TryParse(text.AsSpan(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code):
                    builder.Append((char)code);
                    i += 4;
                    break;
                default: builder.Append(next); break;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Escapes like Java's Properties.store. Anything outside printable ASCII becomes \uXXXX, which every
    /// Minecraft version reads correctly (1.16.5 reads the file as ISO-8859-1, newer ones as UTF-8).
    /// </summary>
    private static string Escape(string text, bool isKey)
    {
        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            switch (c)
            {
                case '\\': builder.Append(@"\\"); break;
                case '\t': builder.Append(@"\t"); break;
                case '\n': builder.Append(@"\n"); break;
                case '\r': builder.Append(@"\r"); break;
                case '\f': builder.Append(@"\f"); break;
                case '=' or ':' or '#' or '!': builder.Append('\\').Append(c); break;
                case ' ' when isKey || i == 0: builder.Append(@"\ "); break;
                case < ' ' or > '~': builder.Append(CultureInfo.InvariantCulture, $"\\u{(int)c:x4}"); break;
                default: builder.Append(c); break;
            }
        }

        return builder.ToString();
    }
}
