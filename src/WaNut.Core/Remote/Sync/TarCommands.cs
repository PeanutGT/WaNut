using WaNut.Core.Remote.Run;

namespace WaNut.Core.Remote.Sync;

/// <summary>
/// The sh commands behind archive transfers. Every path goes through <see cref="RemoteShell"/>, so no file name can
/// change what a command does.
/// </summary>
public static class TarCommands
{
    /// <summary>Where an upload lands before it is moved into place. Starts with ".wanut-", so syncs ignore it.</summary>
    public const string StageFolder = ".wanut-incoming";

    /// <summary>Prints "yes" when the VPS has GNU tar, gzip and a find that can print sizes.</summary>
    public static string ProbeCommand =>
        "sh -c " + RemoteShell.Quote(
            "command -v tar >/dev/null 2>&1 && command -v gzip >/dev/null 2>&1 && " +
            "tar --version 2>/dev/null | grep -q 'GNU tar' && find . -maxdepth 0 -printf '' 2>/dev/null && echo yes || echo no");

    /// <summary>Unpacks a gzip-compressed tar from stdin into a fresh stage folder.</summary>
    public static string ExtractCommand(string root) =>
        "sh -c " + RemoteShell.Quote(
            $"set -e; root={RemoteShell.Home(root)}; stage=\"$root\"/{StageFolder}; rm -rf \"$stage\"; mkdir -p \"$stage\"; " +
            "gzip -dc | tar -xf - --no-same-owner --no-same-permissions -C \"$stage\"");

    /// <summary>Sizes of the files in the stage folder, as "size TAB path" records ending in NUL.</summary>
    public static string StageListCommand(string root) =>
        "sh -c " + RemoteShell.Quote(
            $"cd {RemoteShell.Home(root)}/{StageFolder} && find . -type f -printf '%s\\t%P\\0'");

    /// <summary>Moves each staged file over its real one (a rename, so each file is replaced whole) and removes the stage.</summary>
    public static string MoveCommand(string root) =>
        "sh -c " + RemoteShell.Quote(
            $"set -e; cd {RemoteShell.Home(root)}/{StageFolder}; " +
            "find . -type f -exec sh -c 'for f; do d=${f%/*}; mkdir -p \"../$d\"; mv -f \"$f\" \"../$f\"; done' sh {} +; " +
            $"cd ..; rm -rf {StageFolder}");

    /// <summary>Removes a stage folder left by an upload that was cut short.</summary>
    public static string ClearStageCommand(string root) =>
        "sh -c " + RemoteShell.Quote($"rm -rf {RemoteShell.Home(root)}/{StageFolder}");

    /// <summary>Packs the files named on stdin (NUL separated, relative to the server folder) into gzip-compressed tar on stdout.</summary>
    public static string PackCommand(string root) =>
        "sh -c " + RemoteShell.Quote(
            $"cd {RemoteShell.Home(root)} && tar --null --no-recursion -T - -cf - --use-compress-program='gzip -1'");
}
