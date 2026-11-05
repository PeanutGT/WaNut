namespace WaNut.Core.Remote.Run;

/// <summary>Builds POSIX sh text safely: every value WaNut puts in a VPS command goes through here.</summary>
public static class RemoteShell
{
    /// <summary>One sh word that is exactly <paramref name="value"/>, whatever it contains.</summary>
    public static string Quote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    /// <summary>A path under the SSH user's home folder, such as "wanut/servers/x", as one sh word.</summary>
    public static string Home(string relativePath) => "\"$HOME\"/" + Quote(relativePath.TrimStart('/'));
}
