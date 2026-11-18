namespace WaNut.Core.Workspace;

/// <summary>Keeps a file's own line endings when the editor, which shows Windows ones, saves it.</summary>
public static class LineEndings
{
    public static bool UsesCrLf(string text) => text.Contains("\r\n", StringComparison.Ordinal);

    public static string Apply(string text, bool crlf)
    {
        var unified = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        return crlf ? unified.Replace("\n", "\r\n", StringComparison.Ordinal) : unified;
    }
}
