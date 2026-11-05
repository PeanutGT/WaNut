using WaNut.Core.Downloads;

namespace WaNut.Core.Remote.Sync;

/// <summary>
/// Makes one server folder match another: before a VPS start this PC's folder is uploaded, after the stop the VPS
/// folder comes back. Files are compared by size and modified time and only different ones are copied; files the
/// other side has extra are deleted, except under <see cref="SyncRules.IsKept"/> folders.
/// </summary>
public static class FolderSync
{
    /// <summary>SFTP keeps whole seconds and FAT keeps two, so times this close count as the same.</summary>
    public static readonly TimeSpan TimeTolerance = TimeSpan.FromSeconds(2);

    public static SyncPlan Plan(IReadOnlyList<SyncEntry> source, IReadOnlyList<SyncEntry> target, SyncDirection direction)
    {
        bool Takes(SyncEntry e) => !SyncRules.IsIgnored(e.Path) && !(direction == SyncDirection.Upload && SyncRules.IsKept(e.Path));
        bool MayDelete(SyncEntry e) => !SyncRules.IsIgnored(e.Path) && !SyncRules.IsKept(e.Path);

        var wanted = source.Where(Takes).ToDictionary(e => e.Path, StringComparer.Ordinal);
        var existing = target.ToDictionary(e => e.Path, StringComparer.Ordinal);

        var deleteFiles = new List<SyncEntry>();
        var deleteDirectories = new List<SyncEntry>();
        foreach (var have in target.Where(MayDelete))
        {
            wanted.TryGetValue(have.Path, out var want);
            if (want is not null && want.IsDirectory == have.IsDirectory)
            {
                continue;
            }

            // A folder that became a file, or the other way round, goes too.
            (have.IsDirectory ? deleteDirectories : deleteFiles).Add(have);
        }

        // Everything inside a folder that goes must go first, even what is ignored or kept, or the folder can't be removed.
        var listed = deleteFiles.Concat(deleteDirectories).Select(e => e.Path).ToHashSet(StringComparer.Ordinal);
        foreach (var inside in target.Where(e => !listed.Contains(e.Path) && deleteDirectories.Any(d => IsInside(e.Path, d.Path))).ToList())
        {
            (inside.IsDirectory ? deleteDirectories : deleteFiles).Add(inside);
        }

        var deleted = deleteFiles.Concat(deleteDirectories).Select(e => e.Path).ToHashSet(StringComparer.Ordinal);
        bool Exists(string path) => existing.ContainsKey(path) && !deleted.Contains(path);

        var createDirectories = wanted.Values
            .Where(e => e.IsDirectory && !Exists(e.Path))
            .OrderBy(e => e.Path, StringComparer.Ordinal)
            .ToList();
        var copyFiles = wanted.Values
            .Where(e => !e.IsDirectory && !(Exists(e.Path) && Same(e, existing[e.Path])))
            .OrderBy(e => e.Path, StringComparer.Ordinal)
            .ToList();

        return new SyncPlan(
            deleteFiles.OrderBy(e => e.Path, StringComparer.Ordinal).ToList(),
            deleteDirectories.OrderByDescending(e => e.Path.Count(c => c == '/')).ThenBy(e => e.Path, StringComparer.Ordinal).ToList(),
            createDirectories,
            copyFiles);
    }

    /// <summary>Lists both sides, plans and copies. Progress counts copied bytes out of all bytes to copy.</summary>
    public static async Task<SyncResult> RunAsync(
        ISyncTree source,
        ISyncTree target,
        SyncDirection direction,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var plan = Plan(
            await source.ListAsync(cancellationToken).ConfigureAwait(false),
            await target.ListAsync(cancellationToken).ConfigureAwait(false),
            direction);

        foreach (var file in plan.DeleteFiles)
        {
            await target.DeleteFileAsync(file.Path, cancellationToken).ConfigureAwait(false);
        }

        foreach (var directory in plan.DeleteDirectories)
        {
            await target.DeleteDirectoryAsync(directory.Path, cancellationToken).ConfigureAwait(false);
        }

        foreach (var directory in plan.CreateDirectories)
        {
            await target.CreateDirectoryAsync(directory.Path, cancellationToken).ConfigureAwait(false);
        }

        var total = plan.CopyBytes;
        long done = 0;
        progress?.Report(new DownloadProgress(0, total));
        foreach (var file in plan.CopyFiles)
        {
            var start = done;
            await using var read = await source.OpenReadAsync(file.Path, cancellationToken).ConfigureAwait(false);
            await using var counted = new CountingStream(read, bytes => progress?.Report(new DownloadProgress(start + bytes, total)));
            await target.WriteAsync(file.Path, counted, file.ModifiedUtc, cancellationToken).ConfigureAwait(false);
            done = start + file.Size;
        }

        progress?.Report(new DownloadProgress(total, total));
        return new SyncResult(plan.CopyFiles.Count, plan.DeleteFiles.Count + plan.DeleteDirectories.Count, total);
    }

    private static bool Same(SyncEntry a, SyncEntry b) =>
        !b.IsDirectory && a.Size == b.Size && (a.ModifiedUtc - b.ModifiedUtc).Duration() <= TimeTolerance;

    private static bool IsInside(string path, string folder) => path.StartsWith(folder + "/", StringComparison.Ordinal);
}
