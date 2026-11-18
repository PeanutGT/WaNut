namespace WaNut.Core.Storage;

/// <summary>Gives a file or folder a <see cref="McFileCategory"/> from its relative path alone, so it works the same on this PC and on a VPS.</summary>
public static class McFileClassifier
{
    private static readonly string[] ConfigExtensions = [".properties", ".yml", ".yaml", ".toml", ".json", ".json5", ".conf", ".cfg", ".txt", ".ini"];
    private static readonly string[] ArchiveExtensions = [".zip", ".tar.gz", ".tgz", ".7z"];
    private static readonly string[] WorldFolders = ["world", "world_nether", "world_the_end"];

    /// <param name="path">Relative to the server folder, '/' separators.</param>
    public static McFileCategory Classify(string path, bool isDirectory)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return McFileCategory.Other;
        }

        var name = segments[^1];
        var top = segments[0];
        var lower = name.ToLowerInvariant();

        if (IsWorldFolder(top) || lower.EndsWith(".mca", StringComparison.Ordinal))
        {
            return McFileCategory.WorldData;
        }

        if (top is "logs" or "crash-reports")
        {
            return McFileCategory.LogOrCrash;
        }

        if (top == Backups.BackupStore.FolderName || (!isDirectory && ArchiveExtensions.Any(e => lower.EndsWith(e, StringComparison.Ordinal))))
        {
            return McFileCategory.BackupArchive;
        }

        if (top == "mods-disabled" || lower.EndsWith(".jar.disabled", StringComparison.Ordinal) || lower.EndsWith(".jar.old", StringComparison.Ordinal))
        {
            return McFileCategory.DisabledMod;
        }

        if (top is "mods" or "plugins" && !isDirectory && lower.EndsWith(".jar", StringComparison.Ordinal))
        {
            return McFileCategory.ModOrPlugin;
        }

        if (segments.Length == 1 && (top == "libraries" || (lower.EndsWith(".jar", StringComparison.Ordinal) && !isDirectory)))
        {
            return McFileCategory.ServerCore;
        }

        if (!isDirectory && ConfigExtensions.Any(e => lower.EndsWith(e, StringComparison.Ordinal)))
        {
            return McFileCategory.Configuration;
        }

        return McFileCategory.Other;
    }

    // Paper keeps world_nether / world_the_end beside the main world; a custom level-name is not known here.
    private static bool IsWorldFolder(string top) => WorldFolders.Contains(top, StringComparer.Ordinal);
}
