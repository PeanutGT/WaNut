using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using WaNut.Core.Settings;

namespace WaNut.Core.Backups;

/// <summary>
/// Zips a server's world into its backups/ folder, cleans old backups up and restores one. Only call these while
/// the server is stopped: a running server keeps writing region files, so a copy taken then can be half-saved.
/// <see cref="LiveBackup"/> pauses saving first, so a running server can be backed up too.
/// </summary>
public sealed partial class BackupStore(TimeProvider clock)
{
    public const string FolderName = "backups";

    private const string TimestampFormat = "yyyy-MM-dd_HH-mm-ss";

    // Restores are staged next to the world, on the same drive, so swapping folders is a quick rename.
    private const string StagingPrefix = ".wanut-restore-";

    private static readonly EnumerationOptions AllFiles = new()
    {
        RecurseSubdirectories = true,

        // A junction inside the world could point anywhere on the PC; never follow it.
        AttributesToSkip = FileAttributes.ReparsePoint,
        IgnoreInaccessible = false,
    };

    public static string BackupsDir(string instanceDir) => Path.Combine(instanceDir, FolderName);

    /// <summary>
    /// The world folders that exist: the level-name folder and, for Paper up to 1.21.x, its _nether and _the_end
    /// siblings. From 26.x every dimension lives inside the level-name folder.
    /// </summary>
    public static IReadOnlyList<string> WorldFolders(string instanceDir)
    {
        var level = GameSettingsStore.LevelName(instanceDir);
        if (!IsPlainName(level))
        {
            throw new BackupException(BackupProblem.UnsafeWorldName, level);
        }

        return new[] { level, level + "_nether", level + "_the_end" }
            .Where(name => Directory.Exists(Path.Combine(instanceDir, name)))
            .ToList();
    }

    /// <summary>The server's backups, newest first. Zips WaNut did not name are left out, so they are never deleted.</summary>
    public IReadOnlyList<BackupInfo> List(string instanceDir) => ListFolder(BackupsDir(instanceDir));

    private List<BackupInfo> ListFolder(string dir)
    {
        if (!Directory.Exists(dir))
        {
            return [];
        }

        var backups = new List<BackupInfo>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.zip"))
        {
            if (TryParseName(Path.GetFileName(file), out var createdAt, out var reason))
            {
                backups.Add(new BackupInfo(file, createdAt, reason, new FileInfo(file).Length));
            }
        }

        return backups.OrderByDescending(b => b.CreatedAt).ThenByDescending(b => b.FileName, StringComparer.Ordinal).ToList();
    }

    /// <summary>Zips the world. Returns null when there is no world yet, as before a server's first start.</summary>
    public async Task<BackupInfo?> CreateAsync(
        string instanceDir, BackupReason reason, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var folders = WorldFolders(instanceDir);
        if (folders.Count == 0)
        {
            return null;
        }

        var dir = BackupsDir(instanceDir);
        Directory.CreateDirectory(dir);
        var path = UniquePath(dir, clock.GetLocalNow(), reason);
        var temp = path + ".tmp";
        try
        {
            await Task.Run(() => WriteZip(instanceDir, folders, temp, progress, cancellationToken), cancellationToken);
            File.Move(temp, path);
        }
        catch
        {
            File.Delete(temp);
            throw;
        }

        TryParseName(Path.GetFileName(path), out var createdAt, out _);
        var backup = new BackupInfo(path, createdAt, reason, new FileInfo(path).Length);
        Created?.Invoke(instanceDir, backup);
        return backup;
    }

    /// <summary>Raised after a backup zip was written, with the server folder it belongs to.</summary>
    public event Action<string, BackupInfo>? Created;

    /// <summary>
    /// Copies a backup into <paramref name="copyDir"/> (for example a OneDrive folder), then applies the cleanup
    /// rules there too. The zip is written under a temporary name first, so a half-copied file never looks complete.
    /// </summary>
    public async Task CopyToFolderAsync(BackupInfo backup, string copyDir, BackupRules rules, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(copyDir);
        var target = Path.Combine(copyDir, backup.FileName);
        if (!File.Exists(target))
        {
            var temp = target + ".tmp";
            try
            {
                await using (var input = new FileStream(backup.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
                await using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                {
                    await input.CopyToAsync(output, cancellationToken);
                }

                File.Move(temp, target);
            }
            catch
            {
                File.Delete(temp);
                throw;
            }
        }

        CleanupFolder(copyDir, rules);
    }

    /// <summary>
    /// Deletes backups beyond the newest <see cref="BackupRules.KeepCount"/> and those older than
    /// <see cref="BackupRules.MaxAgeDays"/>. The newest backup is always kept, however old, so a server left alone
    /// for months still has one. The copies made before a restore, a world import or a type/version change do not count toward the
    /// keep count, so a few starts afterwards cannot delete the only way back; they still expire by age. The same
    /// goes for the copy made before a world reset.
    /// </summary>
    public IReadOnlyList<BackupInfo> Cleanup(string instanceDir, BackupRules rules) => CleanupFolder(BackupsDir(instanceDir), rules);

    private List<BackupInfo> CleanupFolder(string dir, BackupRules rules)
    {
        rules = rules.Normalized();
        var oldest = clock.GetUtcNow() - TimeSpan.FromDays(rules.MaxAgeDays);
        var deleted = new List<BackupInfo>();
        var backups = ListFolder(dir);
        var counted = 0;
        for (var i = 0; i < backups.Count; i++)
        {
            var backup = backups[i];
            var isSafetyCopy = backup.Reason is BackupReason.Restore or BackupReason.Change or BackupReason.Import or BackupReason.Reset;
            var tooMany = !isSafetyCopy && counted >= rules.KeepCount;
            if (i > 0 && (tooMany || backup.CreatedAt < oldest))
            {
                File.Delete(backup.FilePath);
                deleted.Add(backup);
            }
            else if (!isSafetyCopy)
            {
                counted++;
            }
        }

        return deleted;
    }

    /// <summary>The parts of the world a backup holds, in Overworld, Nether, End order.</summary>
    public static IReadOnlyList<WorldPart> Parts(string instanceDir, BackupInfo backup)
    {
        var level = GameSettingsStore.LevelName(instanceDir);
        using var archive = OpenZip(backup.FilePath);
        var parts = archive.Entries.Select(entry => PartOf(level, entry.FullName)).ToHashSet();
        return Enum.GetValues<WorldPart>().Where(parts.Contains).ToList();
    }

    /// <summary>Which part of the world a path such as "world/DIM-1/region/r.0.0.mca" belongs to.</summary>
    public static WorldPart PartOf(string level, string path)
    {
        path = path.Replace('\\', '/');
        foreach (var part in new[] { WorldPart.Nether, WorldPart.End })
        {
            if (PartFolders(level, part).Any(folder => path.Equals(folder, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase)))
            {
                return part;
            }
        }

        return WorldPart.Overworld;
    }

    /// <summary>
    /// Where the Nether or End lives in each layout: in its own folder for Paper up to 1.21.x, inside the world up to
    /// 1.21.x, and under dimensions/ from 26.x. The Overworld is everything else.
    /// </summary>
    private static string[] PartFolders(string level, WorldPart part) => part switch
    {
        WorldPart.Nether => [level + "_nether", level + "/DIM-1", level + "/dimensions/minecraft/the_nether"],
        WorldPart.End => [level + "_the_end", level + "/DIM1", level + "/dimensions/minecraft/the_end"],
        _ => [],
    };

    /// <summary>
    /// Puts the backup's world back, or only the chosen <paramref name="parts"/> of it (null for all). The current
    /// world is backed up first (as a "restore" backup), so a restore can itself be undone. Returns that safety
    /// backup, or null when there was no world to save.
    /// </summary>
    public async Task<BackupInfo?> RestoreAsync(
        string instanceDir, BackupInfo backup, IReadOnlyCollection<WorldPart>? parts = null, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(backup.FilePath))
        {
            throw new BackupException(BackupProblem.BackupMissing, backup.FileName);
        }

        var current = WorldFolders(instanceDir);
        var incoming = NewStagingDir(instanceDir, "new");

        // Read the whole zip into a staging folder before touching the world, so a damaged zip changes nothing.
        IReadOnlyList<string> restoredFolders;
        try
        {
            restoredFolders = await Task.Run(() => Extract(backup.FilePath, incoming, progress, cancellationToken), cancellationToken);
            if (parts is not null && Enum.GetValues<WorldPart>().Any(part => !parts.Contains(part)))
            {
                incoming = await Task.Run(() => MixParts(instanceDir, current, incoming, parts, cancellationToken), cancellationToken);
                restoredFolders = Directory.EnumerateDirectories(incoming).Select(dir => Path.GetFileName(dir)).ToList();
            }
        }
        catch
        {
            DeleteQuietly(incoming);
            throw;
        }

        return await ReplaceWorldAsync(instanceDir, incoming, restoredFolders, BackupReason.Restore, cancellationToken);
    }

    /// <summary>
    /// Deletes the world so the next start generates a new one. The world is backed up first (as a "reset" backup);
    /// if that fails, nothing is deleted. Settings, players, mods and plugins stay. Returns the safety backup, or
    /// null when there was no world.
    /// </summary>
    public async Task<BackupInfo?> ResetWorldAsync(string instanceDir, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var safety = await CreateAsync(instanceDir, BackupReason.Reset, progress, cancellationToken);
        var folders = WorldFolders(instanceDir);
        if (folders.Count == 0)
        {
            return safety;
        }

        // Moved aside first, so a folder in use puts everything back instead of leaving half a world.
        var outgoing = NewStagingDir(instanceDir, "old");
        Swap(instanceDir, outgoing, outgoing, folders, []);
        DeleteQuietly(outgoing);
        return safety;
    }

    /// <summary>
    /// Extracts a backup's world folders into <paramref name="target"/>, which must not exist yet, and returns their
    /// names (e.g. "world", "world_nether"). Nothing outside <paramref name="target"/> is touched.
    /// </summary>
    public async Task<IReadOnlyList<string>> ExtractAsync(
        BackupInfo backup, string target, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(backup.FilePath))
        {
            throw new BackupException(BackupProblem.BackupMissing, backup.FileName);
        }

        return await Task.Run(() => Extract(backup.FilePath, target, progress, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Swaps in the world folders staged in <paramref name="incoming"/> (named as in the server folder, e.g. "world").
    /// The current world is backed up first with <paramref name="reason"/>, which should be a safety-copy reason
    /// such as Restore or Import, then moved aside and deleted once the new one is in place. If a move fails, the
    /// old world is put back. Returns the safety backup, or null when there was no world to save.
    /// </summary>
    public async Task<BackupInfo?> ReplaceWorldAsync(
        string instanceDir, string incoming, IReadOnlyList<string> incomingFolders, BackupReason reason, CancellationToken cancellationToken = default)
    {
        BackupInfo? safety;
        try
        {
            safety = await CreateAsync(instanceDir, reason, cancellationToken: cancellationToken);
        }
        catch
        {
            DeleteQuietly(incoming);
            throw;
        }

        var outgoing = NewStagingDir(instanceDir, "old");
        var replaced = WorldFolders(instanceDir).Union(incomingFolders, StringComparer.OrdinalIgnoreCase)
            .Where(name => Directory.Exists(Path.Combine(instanceDir, name)))
            .ToList();
        Swap(instanceDir, incoming, outgoing, replaced, incomingFolders);

        DeleteQuietly(outgoing);
        DeleteQuietly(incoming);
        return safety;
    }

    /// <summary>
    /// Builds the world to swap in from the extracted backup and the current world: the chosen parts come from the
    /// backup, the rest stays as it is now. Returns the staging folder that holds it.
    /// </summary>
    private string MixParts(
        string instanceDir, IReadOnlyList<string> current, string extracted, IReadOnlyCollection<WorldPart> parts, CancellationToken cancellationToken)
    {
        var level = GameSettingsStore.LevelName(instanceDir);
        if (parts.Contains(WorldPart.Overworld))
        {
            // Start from the backup and bring the current Nether or End back in.
            foreach (var part in new[] { WorldPart.Nether, WorldPart.End }.Where(p => !parts.Contains(p)))
            {
                CopyFolders(instanceDir, extracted, PartFolders(level, part), cancellationToken);
            }

            return extracted;
        }

        // Start from a copy of the current world and bring the chosen Nether or End in from the backup.
        var mixed = NewStagingDir(instanceDir, "mix");
        try
        {
            CopyFolders(instanceDir, mixed, current, cancellationToken);
            foreach (var part in parts)
            {
                CopyFolders(extracted, mixed, PartFolders(level, part), cancellationToken);
            }
        }
        catch
        {
            DeleteQuietly(mixed);
            throw;
        }

        DeleteQuietly(extracted);
        return mixed;
    }

    /// <summary>
    /// Replaces each of <paramref name="folders"/> (relative, e.g. "world/DIM-1") in <paramref name="to"/> with a
    /// copy of the one in <paramref name="from"/>. A folder <paramref name="from"/> doesn't have is left alone.
    /// </summary>
    private static void CopyFolders(string from, string to, IEnumerable<string> folders, CancellationToken cancellationToken)
    {
        foreach (var folder in folders)
        {
            var source = Path.Combine(from, folder);
            if (!Directory.Exists(source))
            {
                continue;
            }

            var target = Path.Combine(to, folder);
            if (Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }

            Directory.CreateDirectory(target);
            foreach (var file in new DirectoryInfo(source).EnumerateFiles("*", AllFiles).Where(f => !IsSessionLock(f.Name)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var copy = Path.Combine(target, Path.GetRelativePath(source, file.FullName));
                Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                file.CopyTo(copy);
            }
        }
    }

    private string NewStagingDir(string instanceDir, string kind)
    {
        var stamp = clock.GetUtcNow().ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        for (var n = 1; ; n++)
        {
            var dir = Path.Combine(instanceDir, $"{StagingPrefix}{kind}-{stamp}" + (n == 1 ? string.Empty : $"-{n}"));
            if (!Directory.Exists(dir))
            {
                return dir;
            }
        }
    }

    private static void WriteZip(
        string instanceDir, IReadOnlyList<string> folders, string zipPath, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var files = folders
            .SelectMany(folder => new DirectoryInfo(Path.Combine(instanceDir, folder)).EnumerateFiles("*", AllFiles))
            .Where(file => !IsSessionLock(file.Name))
            .ToList();
        var total = Math.Max(1, files.Sum(f => f.Length));
        long done = 0;

        using var stream = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = archive.CreateEntry(EntryName(instanceDir, file.FullName), CompressionLevel.Fastest);
            entry.LastWriteTime = file.LastWriteTime;
            using (var input = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var output = entry.Open())
            {
                input.CopyTo(output);
            }

            done += file.Length;
            progress?.Report((double)done / total);
        }
    }

    /// <summary>Extracts into <paramref name="target"/> and returns the top-level folder names, e.g. "world".</summary>
    private static IReadOnlyList<string> Extract(string zipPath, string target, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        using (var archive = OpenZip(zipPath))
        {
            var root = Path.GetFullPath(target) + Path.DirectorySeparatorChar;
            var plan = new List<(ZipArchiveEntry Entry, string Destination)>();
            var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in archive.Entries)
            {
                var segments = entry.FullName.Replace('\\', '/').Split('/');
                var isFolder = segments[^1].Length == 0;
                var parts = isFolder ? segments[..^1] : segments;

                // Every entry sits inside a world folder, and no part may climb out of it.
                if ((parts.Length < 2 && !isFolder) || parts.Length == 0 || !parts.All(IsPlainName))
                {
                    throw new BackupException(BackupProblem.DamagedBackup, entry.FullName);
                }

                var destination = Path.GetFullPath(Path.Combine(target, Path.Combine(parts)));
                if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    throw new BackupException(BackupProblem.DamagedBackup, entry.FullName);
                }

                folders.Add(parts[0]);
                if (!isFolder)
                {
                    plan.Add((entry, destination));
                }
            }

            if (plan.Count == 0)
            {
                throw new BackupException(BackupProblem.DamagedBackup, "empty");
            }

            Directory.CreateDirectory(target);
            foreach (var folder in folders)
            {
                Directory.CreateDirectory(Path.Combine(target, folder));
            }

            var total = Math.Max(1, plan.Sum(p => p.Entry.Length));
            long done = 0;
            foreach (var (entry, destination) in plan)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                try
                {
                    entry.ExtractToFile(destination, overwrite: false);
                }
                catch (InvalidDataException e)
                {
                    throw new BackupException(BackupProblem.DamagedBackup, e.Message);
                }

                done += entry.Length;
                progress?.Report((double)done / total);
            }

            return folders.ToList();
        }
    }

    /// <summary>Moves the current world folders aside and the restored ones in. Puts the old ones back if a move fails.</summary>
    private static void Swap(string instanceDir, string incoming, string outgoing, IReadOnlyList<string> replaced, IReadOnlyList<string> restored)
    {
        Directory.CreateDirectory(outgoing);
        var movedOut = new List<string>();
        var movedIn = new List<string>();
        try
        {
            foreach (var name in replaced)
            {
                Directory.Move(Path.Combine(instanceDir, name), Path.Combine(outgoing, name));
                movedOut.Add(name);
            }

            foreach (var name in restored)
            {
                Directory.Move(Path.Combine(incoming, name), Path.Combine(instanceDir, name));
                movedIn.Add(name);
            }
        }
        catch
        {
            // Each folder is put back on its own, so one that can't move doesn't stop the rest; the original error is rethrown.
            foreach (var name in movedIn)
            {
                TryMove(Path.Combine(instanceDir, name), Path.Combine(incoming, name));
            }

            foreach (var name in movedOut)
            {
                TryMove(Path.Combine(outgoing, name), Path.Combine(instanceDir, name));
            }

            throw;
        }
    }

    private static void TryMove(string from, string to)
    {
        try
        {
            Directory.Move(from, to);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Left where it is; the folder still exists on disk and can be moved back by hand.
        }
    }

    private static ZipArchive OpenZip(string zipPath)
    {
        try
        {
            return ZipFile.OpenRead(zipPath);
        }
        catch (InvalidDataException e)
        {
            throw new BackupException(BackupProblem.DamagedBackup, e.Message);
        }
    }

    private static string EntryName(string instanceDir, string file) => Path.GetRelativePath(instanceDir, file).Replace('\\', '/');

    // Minecraft holds session.lock open while running and writes a new one on every start, so it is never worth saving.
    private static bool IsSessionLock(string fileName) => fileName.Equals("session.lock", StringComparison.OrdinalIgnoreCase);

    internal static bool IsPlainName(string name) =>
        name.Length > 0 && name != "." && name != ".." && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && name.IndexOfAny(['/', '\\', ':']) < 0;

    private static string UniquePath(string dir, DateTimeOffset localNow, BackupReason reason)
    {
        var baseName = $"{localNow.ToString(TimestampFormat, CultureInfo.InvariantCulture)}_{reason.ToString().ToLowerInvariant()}";
        for (var n = 1; ; n++)
        {
            var path = Path.Combine(dir, (n == 1 ? baseName : $"{baseName}-{n}") + ".zip");
            if (!File.Exists(path) && !File.Exists(path + ".tmp"))
            {
                return path;
            }
        }
    }

    private bool TryParseName(string fileName, out DateTimeOffset createdAt, out BackupReason reason)
    {
        createdAt = default;
        reason = default;
        var match = NamePattern().Match(fileName);
        if (!match.Success
            || !Enum.TryParse(match.Groups["reason"].Value, ignoreCase: true, out reason)
            || !DateTime.TryParseExact(match.Groups["time"].Value, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            return false;
        }

        var zone = clock.LocalTimeZone;
        createdAt = new DateTimeOffset(local, zone.GetUtcOffset(local));
        return true;
    }

    private static void DeleteQuietly(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A leftover staging folder costs disk space, not data; the world is already in place.
        }
    }

    [GeneratedRegex(@"^(?<time>\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2})_(?<reason>[a-z]+)(-\d+)?\.zip$", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();
}
