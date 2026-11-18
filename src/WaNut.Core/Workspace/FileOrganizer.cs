using WaNut.Core.Backups;
using WaNut.Core.Properties;
using WaNut.Core.Storage;

namespace WaNut.Core.Workspace;

/// <summary>
/// Tidies a server folder on this PC or a VPS in two steps: <see cref="EvaluateAsync"/> lists what could go (a dry
/// run, nothing changes), then <see cref="ExecutePlanAsync"/> deletes or packs only what the person kept checked.
/// The world is never deleted: its folders are refused even if a plan names them. Only call this while the server is
/// stopped, as it writes logs and region files while it runs.
/// </summary>
public sealed class FileOrganizer(IServerFileSystem fileSystem, TimeProvider clock)
{
    private const string PropertiesFileName = "server.properties";

    // Written by the running server; the newest ones are what a crash report or a support question needs.
    private static readonly string[] CurrentLogs = ["logs/latest.log", "logs/debug.log"];

    private static readonly string[] LooseCopyMarkers = ["world_backup", "old_world"];

    private readonly BackupStore _backupNames = new(clock);

    /// <summary>Lists every file once (a single command on a VPS) and applies the rules. Items come largest first.</summary>
    public async Task<CleanupPlan> EvaluateAsync(CleanupRules rules, CancellationToken cancellationToken = default)
    {
        var nodes = await fileSystem.ListDirectoryAsync(string.Empty, true, cancellationToken).ConfigureAwait(false);
        var worlds = await WorldFoldersAsync(cancellationToken).ConfigureAwait(false);
        var now = clock.GetUtcNow();
        var items = new List<CleanupItem>();

        var logCutoff = now.UtcDateTime - TimeSpan.FromDays(Math.Max(rules.LogRetentionDays, 1));
        items.AddRange(nodes
            .Where(n => !n.IsDirectory && n.Category == McFileCategory.LogOrCrash && n.LastModifiedUtc < logCutoff
                && !CurrentLogs.Contains(n.Path, StringComparer.Ordinal))
            .Select(n => new CleanupItem(n, CleanupReason.OldLog, CleanupAction.Delete, n.SizeBytes, false)));

        items.AddRange(nodes
            .Where(n => !n.IsDirectory && n.Category == McFileCategory.DisabledMod)
            .Select(n => new CleanupItem(n, CleanupReason.DisabledMod, CleanupAction.Delete, n.SizeBytes, false)));

        items.AddRange(OldBackups(nodes, rules.Backups, now));
        items.AddRange(LooseWorldCopies(nodes, worlds));

        var ordered = items
            .Where(i => !IsWorld(i.Path, worlds))
            .OrderByDescending(i => i.SizeBytes)
            .ThenBy(i => i.Path, StringComparer.Ordinal)
            .ToList();
        return new CleanupPlan(ordered, now.UtcDateTime);
    }

    /// <summary>
    /// Packs and deletes <paramref name="selected"/>. Packed folders are removed only after their archive is written.
    /// An entry that changed or disappeared since the dry run is skipped, not deleted.
    /// </summary>
    /// <exception cref="CleanupException">An item is part of the world; nothing was changed.</exception>
    public async Task<CleanupResult> ExecutePlanAsync(IEnumerable<CleanupItem> selected, CancellationToken cancellationToken = default)
    {
        var items = selected.DistinctBy(i => i.Path).ToList();
        var worlds = await WorldFoldersAsync(cancellationToken).ConfigureAwait(false);
        if (items.FirstOrDefault(i => IsWorld(i.Path, worlds) || (i.Action == CleanupAction.Delete && i.Node.Category == McFileCategory.WorldData)) is { } world)
        {
            throw new CleanupException(world.Path);
        }

        var current = (await fileSystem.ListDirectoryAsync(string.Empty, true, cancellationToken).ConfigureAwait(false))
            .ToDictionary(n => n.Path, StringComparer.Ordinal);
        var skipped = new List<CleanupItem>();
        var ready = new List<CleanupItem>();
        foreach (var item in items)
        {
            if (current.TryGetValue(item.Path, out var now) && now.IsDirectory == item.Node.IsDirectory
                && (now.IsDirectory || (now.SizeBytes == item.Node.SizeBytes && now.LastModifiedUtc == item.Node.LastModifiedUtc)))
            {
                ready.Add(item);
            }
            else
            {
                skipped.Add(item);
            }
        }

        var archived = 0;
        var toDelete = new List<CleanupItem>();
        foreach (var item in ready)
        {
            if (item.Action == CleanupAction.Archive)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var output = UniqueArchivePath(item.ArchivePath ?? ArchivePathFor(item.Node.Name), current.Keys);
                await fileSystem.CreateArchiveAsync([item.Path], output, cancellationToken).ConfigureAwait(false);
                current[output] = item.Node with { Path = output };
                archived++;
            }

            toDelete.Add(item);
        }

        if (toDelete.Count > 0)
        {
            await fileSystem.DeleteBatchAsync(toDelete.Select(i => i.Path), cancellationToken).ConfigureAwait(false);
        }

        var deleted = toDelete.Count(i => i.Action == CleanupAction.Delete);
        return new CleanupResult(deleted, archived, CleanupPlan.FreedBytes(toDelete), skipped);
    }

    private IEnumerable<CleanupItem> OldBackups(IReadOnlyList<ServerFileNode> nodes, BackupRules rules, DateTimeOffset now)
    {
        var backups = new List<(BackupInfo Info, ServerFileNode Node)>();
        foreach (var node in nodes)
        {
            var slash = node.Path.IndexOf('/', StringComparison.Ordinal);
            if (!node.IsDirectory && slash > 0 && node.Path[..slash] == BackupStore.FolderName && node.Path.IndexOf('/', slash + 1) < 0
                && _backupNames.TryParseName(node.Name, out var createdAt, out var reason))
            {
                backups.Add((new BackupInfo(node.Path, createdAt, reason, node.SizeBytes), node));
            }
        }

        var newestFirst = backups
            .OrderByDescending(b => b.Info.CreatedAt)
            .ThenByDescending(b => b.Info.FileName, StringComparer.Ordinal)
            .ToList();
        var expired = BackupStore.Expired(newestFirst.Select(b => b.Info).ToList(), rules, now).Select(b => b.FilePath).ToHashSet(StringComparer.Ordinal);
        return newestFirst
            .Where(b => expired.Contains(b.Node.Path))
            .Select(b => new CleanupItem(b.Node, CleanupReason.OldBackup, CleanupAction.Delete, b.Node.SizeBytes, false));
    }

    private static IEnumerable<CleanupItem> LooseWorldCopies(IReadOnlyList<ServerFileNode> nodes, IReadOnlyCollection<string> worlds)
    {
        foreach (var folder in nodes.Where(n => n.IsDirectory && !n.Path.Contains('/', StringComparison.Ordinal)))
        {
            var lower = folder.Name.ToLowerInvariant();
            if (!LooseCopyMarkers.Any(m => lower.Contains(m, StringComparison.Ordinal)) || worlds.Contains(folder.Name))
            {
                continue;
            }

            var inside = nodes.Where(n => !n.IsDirectory && n.Path.StartsWith(folder.Path + "/", StringComparison.Ordinal)).ToList();
            var risky = inside.Any(n => n.Category == McFileCategory.WorldData || n.Name.EndsWith(".mca", StringComparison.OrdinalIgnoreCase));
            yield return new CleanupItem(folder, CleanupReason.LooseWorldCopy, CleanupAction.Archive, inside.Sum(n => n.SizeBytes), risky, ArchivePathFor(folder.Name));
        }
    }

    /// <summary>The level-name folder and its Paper siblings, read from server.properties ("world" when unset).</summary>
    private async Task<IReadOnlyCollection<string>> WorldFoldersAsync(CancellationToken cancellationToken)
    {
        var level = (await PropertiesFile.LoadAsync(fileSystem, PropertiesFileName, cancellationToken).ConfigureAwait(false)).Get("level-name");
        level = string.IsNullOrWhiteSpace(level) ? "world" : level.Trim().Replace('\\', '/');
        var names = new HashSet<string>(StringComparer.Ordinal) { "world", "world_nether", "world_the_end" };
        foreach (var name in new[] { level, level + "_nether", level + "_the_end" })
        {
            // A level-name with folders in it still protects its top folder.
            names.Add(name.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "world");
        }

        return names;
    }

    private static bool IsWorld(string path, IReadOnlyCollection<string> worlds)
    {
        var top = path.Split('/', 2)[0];
        return worlds.Contains(top);
    }

    private static string ArchivePathFor(string folderName) => $"{BackupStore.FolderName}/{folderName}.tar.gz";

    private static string UniqueArchivePath(string wanted, IEnumerable<string> taken)
    {
        var used = taken.ToHashSet(StringComparer.Ordinal);
        var stem = wanted[..^".tar.gz".Length];
        for (var n = 1; ; n++)
        {
            var path = n == 1 ? wanted : $"{stem}-{n}.tar.gz";
            if (!used.Contains(path))
            {
                return path;
            }
        }
    }
}
