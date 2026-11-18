using WaNut.Core.Remote.Run;

namespace WaNut.Core.Remote.Files;

/// <summary>
/// The sh commands behind <see cref="RemoteVpsAdapter"/>. Every path goes through <see cref="RemoteShell"/>, and each
/// command first checks that the server folder is still the one that was opened.
/// </summary>
public static class RemoteFileCommands
{
    /// <summary>The server folder doesn't exist (or can't be entered).</summary>
    public const int NoFolderExit = 3;

    /// <summary>A path to delete goes through a link that leaves the server folder.</summary>
    public const int OutsideExit = 4;

    /// <summary>Delete: the folder has none of <see cref="WorkspaceRootGuard.Markers"/>. Archive: a target is missing.</summary>
    public const int MissingExit = 5;

    /// <summary>The folder's real path is no longer the one that was opened.</summary>
    public const int MovedExit = 6;

    /// <summary>Written to stderr by the delete command before <see cref="OutsideExit"/>, since xargs hides the exit code.</summary>
    public const string OutsideMarker = "WANUT-OUTSIDE";

    /// <summary>The user's real home, NUL, then the folder's real path. Exits with <see cref="NoFolderExit"/> when it doesn't exist.</summary>
    /// <param name="root">Absolute, or relative to the SSH user's home.</param>
    public static string Resolve(string root) =>
        Sh($"h=$(cd \"$HOME\" && pwd -P) || exit 1; cd {RemoteShell.Folder(root)} 2>/dev/null || exit {NoFolderExit}; " +
           "printf '%s\\0%s' \"$h\" \"$(pwd -P)\"");

    /// <summary>
    /// Deletes the paths read from stdin (NUL separated, relative to <paramref name="root"/>, already normalized). Refuses
    /// a folder without <see cref="WorkspaceRootGuard.Markers"/> and, before deleting anything in a batch, any path
    /// whose parent folder really lies outside <paramref name="root"/>. Paths that are gone are skipped.
    /// </summary>
    /// <param name="root">The real absolute path from <see cref="Resolve"/>.</param>
    public static string Delete(string root)
    {
        var quotedRoot = RemoteShell.Quote(root);

        // "$(... && echo x)" then "%??" keeps a trailing newline in a folder name from shortening the path.
        var perBatch =
            "for f; do " +
            "[ -e \"$f\" ] || [ -L \"$f\" ] || continue; " +
            "case $f in */*) d=${f%/*};; *) d=.;; esac; " +
            "real=$(cd -- \"$d\" && pwd -P && echo x) || { printf '" + OutsideMarker + "\\n' >&2; exit " + OutsideExit + "; }; real=${real%??}; " +
            $"case \"$real/\" in {quotedRoot}/*) ;; *) printf '{OutsideMarker}\\n' >&2; exit {OutsideExit};; esac; " +
            "done; " +
            "for f; do rm -rf -- \"$f\" || exit 1; done";

        return Sh(
            $"{Enter(root)}; " +
            $"{{ [ -f server.properties ] || [ -f eula.txt ] || [ -d libraries ]; }} || exit {MissingExit}; " +
            $"xargs -0 sh -c {RemoteShell.Quote(perBatch)} sh");
    }

    /// <summary>
    /// Packs <paramref name="targets"/> into a gzip-compressed tar at <paramref name="output"/>: written under
    /// <paramref name="temp"/> in the server folder's top level and renamed, so a cut-short pack never looks finished.
    /// </summary>
    public static string Archive(string root, IReadOnlyList<string> targets, string output, string temp)
    {
        var words = string.Join(' ', targets.Select(RemoteShell.Quote));
        var outputFolder = output.Contains('/') ? output[..output.LastIndexOf('/')] : ".";
        return Sh(
            $"{Enter(root)}; " +
            $"for f in {words}; do [ -e \"$f\" ] || exit {MissingExit}; done; " +
            $"trap {RemoteShell.Quote("rm -f -- " + RemoteShell.Quote(temp))} EXIT; " +
            $"mkdir -p -- {RemoteShell.Quote(outputFolder)} && " +
            $"tar -czf {RemoteShell.Quote(temp)} -- {words} && " +
            $"mv -f -- {RemoteShell.Quote(temp)} {RemoteShell.Quote(output)}");
    }

    /// <summary>Enters the folder and checks it is still the real path that was opened.</summary>
    private static string Enter(string root) =>
        $"cd {RemoteShell.Quote(root)} 2>/dev/null || exit {NoFolderExit}; [ \"$(pwd -P)\" = {RemoteShell.Quote(root)} ] || exit {MovedExit}";

    // The user's login shell may not be POSIX (fish, csh), so every command runs in sh.
    private static string Sh(string script) => "sh -c " + RemoteShell.Quote(script);
}
