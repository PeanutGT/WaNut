using WaNut.Core.Remote.Run;

namespace WaNut.Core.Hotplug;

/// <summary>
/// The sh commands that move data packs between the staging, backup and datapacks folders. All three are on one
/// mount (<see cref="HotplugWorkspaceInitializer"/>), so every mv is a rename(2): the datapacks folder only ever holds
/// a complete old file or a complete new one, never a half-copied one, and an update never leaves the name empty.
/// </summary>
public static class HotplugSwapCommands
{
    /// <summary>Hash: the file isn't there.</summary>
    public const int MissingExit = 11;

    /// <summary>Hash: none of sha256sum, shasum or openssl is installed.</summary>
    public const int NoHashToolExit = 12;

    /// <summary>Swap: the name in the datapacks folder is a folder or a link, not a zip; nothing of that item was touched.</summary>
    public const int NotAFileExit = 13;

    /// <summary>Swap: the backup name is taken already.</summary>
    public const int BackupTakenExit = 14;

    /// <summary>Swap: a move or the backup copy failed.</summary>
    public const int MoveFailedExit = 15;

    /// <summary>Prints the file's SHA-256 in lower-case hex.</summary>
    public static string Sha256(string root, string path) =>
        HotplugCommands.Sh(
            $"{HotplugCommands.Enter(root)}; f={RemoteShell.Quote(path)}; [ -f \"$f\" ] || exit {MissingExit}; " +
            "if command -v sha256sum >/dev/null 2>&1; then h=$(sha256sum -- \"$f\") || exit 1; " +
            "elif command -v shasum >/dev/null 2>&1; then h=$(shasum -a 256 -- \"$f\") || exit 1; " +
            "elif command -v openssl >/dev/null 2>&1; then h=$(openssl dgst -sha256 -r -- \"$f\") || exit 1; " +
            $"else exit {NoHashToolExit}; fi; printf '%s' \"${{h%% *}}\"");

    /// <summary>Renames the checked upload from its .part name to the name it goes in with, still inside the staging folder.</summary>
    public static string Promote(string root, string part, string staged) =>
        HotplugCommands.Sh($"{HotplugCommands.Enter(root)}; mv -f -- {RemoteShell.Quote(part)} {RemoteShell.Quote(staged)}");

    /// <summary>
    /// Applies the steps in order and prints one line per step done ("added", "replaced" or "retired"), so a failure
    /// part way tells exactly what to undo. Replacing first hard-links the old file into the backup folder (a copy where
    /// links aren't allowed) and then renames the new one over it, so the old file stays in place until the new one is.
    /// </summary>
    public static string Swap(string root, IReadOnlyList<HotplugStep> steps)
    {
        var put =
            $"put() {{ [ -f \"$1\" ] || exit {MissingExit}; " +
            "if [ -e \"$2\" ] || [ -L \"$2\" ]; then " +
            $"{{ [ -f \"$2\" ] && [ ! -L \"$2\" ]; }} || exit {NotAFileExit}; " +
            $"{{ [ -e \"$3\" ] || [ -L \"$3\" ]; }} && exit {BackupTakenExit}; " +
            $"ln -- \"$2\" \"$3\" 2>/dev/null || cp -p -- \"$2\" \"$3\" || exit {MoveFailedExit}; " +
            $"mv -f -- \"$1\" \"$2\" || exit {MoveFailedExit}; echo replaced; " +
            $"else mv -- \"$1\" \"$2\" || exit {MoveFailedExit}; echo added; fi; }}; ";
        var retire =
            $"retire() {{ {{ [ -f \"$1\" ] && [ ! -L \"$1\" ]; }} || exit {NotAFileExit}; " +
            $"{{ [ -e \"$2\" ] || [ -L \"$2\" ]; }} && exit {BackupTakenExit}; " +
            $"mv -- \"$1\" \"$2\" || exit {MoveFailedExit}; echo retired; }}; ";
        var calls = string.Join("; ", steps.Select(s => s.Kind == HotplugStepKind.Retire
            ? $"retire {RemoteShell.Quote(s.Target)} {RemoteShell.Quote(s.Backup)}"
            : $"put {RemoteShell.Quote(s.Staged!)} {RemoteShell.Quote(s.Target)} {RemoteShell.Quote(s.Backup)}"));
        return HotplugCommands.Sh($"{HotplugCommands.Enter(root)}; {put}{retire}{calls}");
    }

    /// <summary>
    /// Undoes swapped steps, newest first: an added pack is deleted, a replaced or retired one is renamed back from its
    /// backup. Keeps going past a failed step so as much as possible is restored, and exits 1 when any failed.
    /// </summary>
    public static string Undo(string root, IReadOnlyList<(HotplugStep Step, HotplugSwapResult Done)> swapped)
    {
        var calls = swapped.Reverse().Select(s => s.Done == HotplugSwapResult.Added
            ? $"rm -f -- {RemoteShell.Quote(s.Step.Target)} || bad=1"
            : $"mv -f -- {RemoteShell.Quote(s.Step.Backup)} {RemoteShell.Quote(s.Step.Target)} || bad=1");
        return HotplugCommands.Sh($"{HotplugCommands.Enter(root)}; bad=0; {string.Join("; ", calls)}; exit $bad");
    }

    /// <summary>Deletes leftover uploads; a path that is gone already is fine.</summary>
    public static string Clean(string root, IEnumerable<string> paths) =>
        HotplugCommands.Sh($"{HotplugCommands.Enter(root)}; rm -f -- {string.Join(' ', paths.Select(RemoteShell.Quote))}");

    /// <summary>Appends standard input to the audit file, making its folder when needed.</summary>
    public static string Append(string root, string file)
    {
        var folder = file.Contains('/') ? file[..file.LastIndexOf('/')] : ".";
        return HotplugCommands.Sh($"{HotplugCommands.Enter(root)}; mkdir -p -- {RemoteShell.Quote(folder)} && cat >> {RemoteShell.Quote(file)}");
    }
}
