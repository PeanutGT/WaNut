namespace WaNut.Core.Remote.Sync;

/// <summary>Which paths in a server folder take part in a sync. Paths are relative and use '/'.</summary>
public static class SyncRules
{
    /// <summary>Written by the server and kept on both sides: downloaded when changed, never uploaded or deleted.</summary>
    private static readonly string[] KeptFolders = ["logs", "crash-reports"];

    /// <summary>
    /// Never synced either way: WaNut's own files (backups stay on this PC; instance.json is only read here), the
    /// world lock the server makes itself, and WaNut's half-written temporary files.
    /// </summary>
    public static bool IsIgnored(string path)
    {
        if (path == Instances.InstanceMetadata.FileName || IsUnder(path, Backups.BackupStore.FolderName))
        {
            return true;
        }

        var segments = path.Split('/');
        var name = segments[^1];
        return name == "session.lock"
            || name.EndsWith(".wanut-part", StringComparison.Ordinal)
            || name.EndsWith(".wanut-new", StringComparison.Ordinal)
            || segments.Any(s => s.StartsWith(".wanut-", StringComparison.Ordinal));
    }

    public static bool IsKept(string path) => KeptFolders.Any(folder => IsUnder(path, folder));

    private static bool IsUnder(string path, string folder) =>
        path == folder || path.StartsWith(folder + "/", StringComparison.Ordinal);
}
