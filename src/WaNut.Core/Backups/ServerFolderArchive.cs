using WaNut.Core.Storage;

namespace WaNut.Core.Backups;

/// <summary>
/// A safety copy made inside the server folder itself, where it is (in practice on the VPS, so nothing has to come
/// back to this PC first): the world folders and the mods or plugins, packed into one .tar.gz under
/// <see cref="BackupStore.FolderName"/>. That folder never syncs, so the copy stays where it was made.
/// </summary>
public static class ServerFolderArchive
{
    /// <summary>Packs every top-level folder holding a level.dat, plus <paramref name="extraFolders"/> that exist.</summary>
    /// <param name="reason">A short word for the file name, such as "mod-update".</param>
    /// <returns>The archive's path in the server folder, or null when there was nothing to pack.</returns>
    public static async Task<string?> CreateAsync(
        IServerFileSystem files,
        string reason,
        IReadOnlyList<string> extraFolders,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        var targets = new List<string>();
        foreach (var folder in (await files.ListDirectoryAsync(string.Empty, cancellationToken: cancellationToken).ConfigureAwait(false)).Where(n => n.IsDirectory))
        {
            if (folder.Name != BackupStore.FolderName && await files.ExistsAsync($"{folder.Path}/level.dat", cancellationToken).ConfigureAwait(false))
            {
                targets.Add(folder.Path);
            }
        }

        foreach (var extra in extraFolders)
        {
            if (!targets.Contains(extra, StringComparer.Ordinal) && await files.ExistsAsync(extra, cancellationToken).ConfigureAwait(false))
            {
                targets.Add(extra);
            }
        }

        if (targets.Count == 0)
        {
            return null;
        }

        var name = $"{BackupStore.FolderName}/{nowUtc:yyyy-MM-dd_HH-mm-ss}-{reason}.tar.gz";
        return await files.CreateArchiveAsync(targets, name, cancellationToken).ConfigureAwait(false);
    }
}
