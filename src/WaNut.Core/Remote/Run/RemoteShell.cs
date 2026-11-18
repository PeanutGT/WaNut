namespace WaNut.Core.Remote.Run;

/// <summary>Builds POSIX sh text safely: every value WaNut puts in a VPS command goes through here.</summary>
public static class RemoteShell
{
    /// <summary>One sh word that is exactly <paramref name="value"/>, whatever it contains.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> contains a NUL character, which no sh word can hold.</exception>
    public static string Quote(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("A VPS command can't contain a NUL character.", nameof(value));
        }

        return "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
    }

    /// <summary>A path under the SSH user's home folder, such as "wanut/servers/x", as one sh word.</summary>
    public static string Home(string relativePath) => "\"$HOME\"/" + Quote(relativePath.TrimStart('/'));

    /// <summary>A folder as one sh word: an absolute path as it is, anything else under the SSH user's home (see <see cref="Home"/>).</summary>
    public static string Folder(string path) => path.StartsWith('/') ? Quote(path) : Home(path);
}
