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

    /// <summary>Files copied, deleted or folders made at the same time. OpenSSH serves them all on one SFTP channel.</summary>
    public const int MaxParallel = 8;

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
            deleteDirectories.OrderByDescending(Depth).ThenBy(e => e.Path, StringComparer.Ordinal).ToList(),
            createDirectories,
            copyFiles);
    }

    /// <summary>
    /// Lists both sides, plans and copies. Progress counts copied bytes out of all bytes to copy.
    /// </summary>
    /// <exception cref="VpsFolderMissingException">A download whose source isn't a server folder; nothing was changed.</exception>
    public static async Task<SyncResult> RunAsync(
        ISyncTree source,
        ISyncTree target,
        SyncDirection direction,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var sourceEntries = await source.ListAsync(cancellationToken).ConfigureAwait(false);

        // A missing or emptied folder on the VPS (reinstalled, another sign-in user) lists as nothing, and making this
        // PC match "nothing" would delete the world, mods and settings here.
        if (direction == SyncDirection.Download && !IsServerFolder(sourceEntries))
        {
            throw new VpsFolderMissingException();
        }

        var plan = Plan(sourceEntries, await target.ListAsync(cancellationToken).ConfigureAwait(false), direction);

        // Every SFTP call waits a full round trip to the VPS, so one call at a time spends most of the sync waiting;
        // several in flight keep the line busy.
        var options = new ParallelOptions { MaxDegreeOfParallelism = MaxParallel, CancellationToken = cancellationToken };

        await Parallel.ForEachAsync(plan.DeleteFiles, options, (file, token) => new ValueTask(target.DeleteFileAsync(file.Path, token)))
            .ConfigureAwait(false);

        // Deepest first, as planned, since a folder must be empty to go.
        foreach (var directory in plan.DeleteDirectories)
        {
            await target.DeleteDirectoryAsync(directory.Path, cancellationToken).ConfigureAwait(false);
        }

        // A folder's parent is one level up, so each level can be made side by side once the one above exists.
        foreach (var level in plan.CreateDirectories.GroupBy(Depth).OrderBy(g => g.Key))
        {
            await Parallel.ForEachAsync(level, options, (directory, token) => new ValueTask(target.CreateDirectoryAsync(directory.Path, token)))
                .ConfigureAwait(false);
        }

        var total = plan.CopyBytes;
        long done = 0;
        var reportLock = new object();
        progress?.Report(new DownloadProgress(0, total));

        // Names that differ only in case are one file on Windows, so those go one after another.
        var groups = plan.CopyFiles.GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase);
        await Parallel.ForEachAsync(groups, options, async (group, token) =>
        {
            foreach (var file in group)
            {
                await CopyAsync(file, token).ConfigureAwait(false);
            }
        }).ConfigureAwait(false);

        async Task CopyAsync(SyncEntry file, CancellationToken token)
        {
            long counted = 0;
            void Count(long bytes)
            {
                // Under the lock so the reports reach the window in order and the bar never steps back.
                lock (reportLock)
                {
                    done += bytes - counted;
                    counted = bytes;
                    progress?.Report(new DownloadProgress(done, total));
                }
            }

            // Each file still lands under a temporary name first, so one cut short never replaces the real one.
            await using var read = await source.OpenReadAsync(file.Path, token).ConfigureAwait(false);
            await using var stream = new CountingStream(read, Count);
            await target.WriteAsync(file.Path, stream, file.ModifiedUtc, token).ConfigureAwait(false);
        }

        progress?.Report(new DownloadProgress(total, total));
        return new SyncResult(plan.CopyFiles.Count, plan.DeleteFiles.Count + plan.DeleteDirectories.Count, total);
    }

    /// <summary>Every server folder WaNut uploads, and every server that has run, has a server.properties file.</summary>
    public static bool IsServerFolder(IReadOnlyList<SyncEntry> entries) =>
        entries.Any(e => !e.IsDirectory && e.Path == Settings.GameSettingsStore.PropertiesFileName);

    private static bool Same(SyncEntry a, SyncEntry b) =>
        !b.IsDirectory && a.Size == b.Size && (a.ModifiedUtc - b.ModifiedUtc).Duration() <= TimeTolerance;

    private static int Depth(SyncEntry e) => e.Path.Count(c => c == '/');

    private static bool IsInside(string path, string folder) => path.StartsWith(folder + "/", StringComparison.Ordinal);
}
