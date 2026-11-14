using System.Text.RegularExpressions;

namespace WaNut.Core.Mods;

/// <summary>
/// Turns a Modrinth project's Markdown (often mixed with HTML) into plain text for the details window: pictures and
/// tags go, links keep their words, headings and list marks lose their symbols.
/// </summary>
public static partial class MarkdownText
{
    public static string ToPlain(string markdown)
    {
        var text = markdown.Replace("\r\n", "\n", StringComparison.Ordinal);
        text = Image().Replace(text, string.Empty);
        text = Link().Replace(text, "$1");
        text = HtmlComment().Replace(text, string.Empty);
        text = HtmlTag().Replace(text, string.Empty);
        text = Heading().Replace(text, string.Empty);
        text = Emphasis().Replace(text, "$2");
        text = InlineCode().Replace(text, "$1");
        text = Bullet().Replace(text, "• ");
        text = Rule().Replace(text, string.Empty);
        text = System.Net.WebUtility.HtmlDecode(text);
        text = BlankLines().Replace(text, "\n\n");
        return text.Trim();
    }

    [GeneratedRegex(@"!\[[^\]]*\]\([^)]*\)")]
    private static partial Regex Image();

    [GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex HtmlComment();

    [GeneratedRegex(@"<[^>\n]+>")]
    private static partial Regex HtmlTag();

    [GeneratedRegex(@"^[ \t]{0,3}#{1,6}[ \t]*", RegexOptions.Multiline)]
    private static partial Regex Heading();

    [GeneratedRegex(@"(\*\*|\*)(\S(?:.*?\S)?)\1")]
    private static partial Regex Emphasis();

    [GeneratedRegex(@"`([^`]*)`")]
    private static partial Regex InlineCode();

    [GeneratedRegex(@"^[ \t]*[-*+][ \t]+", RegexOptions.Multiline)]
    private static partial Regex Bullet();

    [GeneratedRegex(@"^[ \t]*([-*_][ \t]*){3,}$", RegexOptions.Multiline)]
    private static partial Regex Rule();

    [GeneratedRegex(@"\n[ \t]*(\n[ \t]*)+")]
    private static partial Regex BlankLines();
}
