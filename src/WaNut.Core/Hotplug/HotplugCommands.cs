using WaNut.Core.Remote.Files;
using WaNut.Core.Remote.Run;

namespace WaNut.Core.Hotplug;

/// <summary>The sh commands behind the hot-plug workspace and its lock. Every path goes through <see cref="RemoteShell"/>.</summary>
public static class HotplugCommands
{
    /// <summary>Probe: the world folder doesn't exist yet (the server has never made it).</summary>
    public const int NoWorldExit = 7;

    /// <summary>Lock: another hot-plug holds it. The holder's lock file is printed.</summary>
    public const int LockHeldExit = 9;

    /// <summary>
    /// Prints the mount point of the server folder, NUL, then the mount point of the world's datapacks folder (or the
    /// world folder when it has none yet). An empty answer means <c>df</c> couldn't tell.
    /// </summary>
    public static string Probe(string root, string worldFolder)
    {
        var datapacks = worldFolder + "/" + HotplugWorkspace.DatapacksName;
        return Sh(
            $"{Enter(root)}; [ -d {RemoteShell.Quote(worldFolder)} ] || exit {NoWorldExit}; " +
            $"d={RemoteShell.Quote(datapacks)}; [ -d \"$d\" ] || d={RemoteShell.Quote(worldFolder)}; " +
            "m() { df -P -- \"$1\" 2>/dev/null | awk 'NR==2{$1=$2=$3=$4=$5=\"\"; sub(/^ +/,\"\"); print}'; }; " +
            "printf '%s\\0%s' \"$(m .)\" \"$(m \"$d\")\"");
    }

    /// <summary>Makes the folders (and the world's datapacks folder, which Minecraft makes itself later). Existing ones stay as they are.</summary>
    public static string Create(string root, HotplugWorkspace workspace) =>
        Sh($"{Enter(root)}; mkdir -p -- {RemoteShell.Quote(workspace.StagingFolder)} {RemoteShell.Quote(workspace.BackupFolder)} {RemoteShell.Quote(workspace.DatapacksFolder)}");

    /// <summary>
    /// Takes the lock file when there is none or the one there wasn't refreshed for <paramref name="staleSeconds"/>
    /// (by the VPS's own clock, so this PC's clock doesn't matter). The file is made with noclobber, so of two
    /// callers only one creates it. Exits <see cref="LockHeldExit"/> and prints the holder's file otherwise.
    /// </summary>
    /// <param name="lines">The file's identifying lines (key=value, no newline in a value); a time= line is added.</param>
    public static string AcquireLock(string root, string lockFile, IReadOnlyList<string> lines, int staleSeconds)
    {
        var f = RemoteShell.Quote(lockFile);
        var printed = string.Join(' ', lines.Select(RemoteShell.Quote));
        return Sh(
            $"{Enter(root)}; f={f}; now=$(date +%s); " +
            "if [ -e \"$f\" ]; then " +
            "t=$(sed -n 's/^time=//p' \"$f\" | head -n 1); case $t in ''|*[!0-9]*) t=0;; esac; " +
            $"if [ $((now - t)) -lt {staleSeconds} ]; then cat -- \"$f\"; exit {LockHeldExit}; fi; " +
            "rm -f -- \"$f\"; fi; " +
            $"( set -C; printf '%s\\n' {printed} \"time=$now\" > \"$f\" ) 2>/dev/null || {{ cat -- \"$f\" 2>/dev/null; exit {LockHeldExit}; }}");
    }

    /// <summary>Sets the lock's time to now, only while the file still carries <paramref name="ownerLine"/>; otherwise exits <see cref="LockHeldExit"/>.</summary>
    public static string RefreshLock(string root, string lockFile, string ownerLine)
    {
        var f = RemoteShell.Quote(lockFile);
        return Sh(
            $"{Enter(root)}; f={f}; grep -qxF -- {RemoteShell.Quote(ownerLine)} \"$f\" 2>/dev/null || exit {LockHeldExit}; " +
            "sed \"s/^time=.*/time=$(date +%s)/\" \"$f\" > \"$f.new\" && mv -f -- \"$f.new\" \"$f\"");
    }

    /// <summary>Removes the lock file only while it still carries <paramref name="ownerLine"/>, so a lock taken over after going stale is left alone.</summary>
    public static string ReleaseLock(string root, string lockFile, string ownerLine)
    {
        var f = RemoteShell.Quote(lockFile);
        return Sh($"{Enter(root)}; f={f}; if grep -qxF -- {RemoteShell.Quote(ownerLine)} \"$f\" 2>/dev/null; then rm -f -- \"$f\"; fi");
    }

    private static string Enter(string root) =>
        $"cd {RemoteShell.Quote(root)} 2>/dev/null || exit {RemoteFileCommands.NoFolderExit}; [ \"$(pwd -P)\" = {RemoteShell.Quote(root)} ] || exit {RemoteFileCommands.MovedExit}";

    private static string Sh(string script) => "sh -c " + RemoteShell.Quote(script);
}
