using System.IO.Compression;
using WaNut.Core.Backups;

namespace WaNut.Core.Worlds;

/// <summary>Copies world folders in and out of zips with the same safety rules as backups.</summary>
internal static class WorldFiles
{
    // Room left over on the drive after a copy, so Windows and the server itself still have space to work.
    private const long SpareBytes = 64L * 1024 * 1024;

    private static readonly EnumerationOptions AllFiles = new()
    {
        RecurseSubdirectories = true,

        // A junction inside the world could point anywhere on the PC; never follow it.
        AttributesToSkip = FileAttributes.ReparsePoint,
        IgnoreInaccessible = false,
    };

    public static IEnumerable<FileInfo> Files(string dir) =>
        new DirectoryInfo(dir).EnumerateFiles("*", AllFiles).Where(f => !IsSessionLock(f.Name));

    public static long FolderSize(string dir) => Files(dir).Sum(f => f.Length);

    /// <summary>The unpacked size of the files under <paramref name="root"/> in the zip.</summary>
    public static long ZipSize(string zipPath, string root)
    {
        using var archive = OpenZip(zipPath);
        return archive.Entries.Where(e => Normalize(e.FullName).StartsWith(root, StringComparison.Ordinal)).Sum(e => e.Length);
    }

    /// <exception cref="WorldException">NotEnoughSpace when the drive holding <paramref name="dir"/> can't take <paramref name="bytes"/>.</exception>
    public static void EnsureFreeSpace(string dir, long bytes)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(dir));
        if (string.IsNullOrEmpty(root))
        {
            return;
        }

        long free;
        try
        {
            free = new DriveInfo(root).AvailableFreeSpace;
        }
        catch (ArgumentException)
        {
            // A network share (\\server\share) has no drive to ask; the copy itself fails cleanly if it runs out.
            return;
        }

        if (free < bytes + SpareBytes)
        {
            throw new WorldException(WorldProblem.NotEnoughSpace, $"{bytes} needed, {free} free");
        }
    }

    /// <summary>Copies a folder, without session.lock and without following junctions. Reports copied bytes.</summary>
    public static void CopyFolder(string from, string to, Action<long> copied, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(to);
        foreach (var dir in new DirectoryInfo(from).EnumerateDirectories("*", AllFiles))
        {
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir.FullName)));
        }

        foreach (var file in Files(from))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = Path.Combine(to, Path.GetRelativePath(from, file.FullName));
            using (var input = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write))
            {
                input.CopyTo(output);
            }

            File.SetLastWriteTimeUtc(target, file.LastWriteTimeUtc);
            copied(file.Length);
        }
    }

    /// <summary>
    /// Extracts the files under <paramref name="root"/> in the zip into <paramref name="target"/>. Every path is
    /// checked first, so a zip with a path that climbs out ("../") changes nothing.
    /// </summary>
    public static void ExtractFolder(string zipPath, string root, string target, Action<long> copied, CancellationToken cancellationToken)
    {
        using var archive = OpenZip(zipPath);
        var full = Path.GetFullPath(target) + Path.DirectorySeparatorChar;
        var plan = new List<(ZipArchiveEntry Entry, string Destination)>();
        foreach (var entry in archive.Entries)
        {
            var name = Normalize(entry.FullName);
            if (!name.StartsWith(root, StringComparison.Ordinal) || name.EndsWith('/'))
            {
                continue;
            }

            var parts = name[root.Length..].Split('/');
            if (!parts.All(BackupStore.IsPlainName))
            {
                throw new WorldException(WorldProblem.DamagedZip, entry.FullName);
            }

            var destination = Path.GetFullPath(Path.Combine(target, Path.Combine(parts)));
            if (!destination.StartsWith(full, StringComparison.OrdinalIgnoreCase))
            {
                throw new WorldException(WorldProblem.DamagedZip, entry.FullName);
            }

            if (!IsSessionLock(parts[^1]))
            {
                plan.Add((entry, destination));
            }
        }

        Directory.CreateDirectory(target);
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
                throw new WorldException(WorldProblem.DamagedZip, e.Message);
            }

            copied(entry.Length);
        }
    }

    /// <summary>Zips a folder with every entry under <paramref name="rootName"/>/, as a world folder zipped from Explorer.</summary>
    public static void ZipFolder(string dir, string rootName, string zipPath, Action<long> copied, CancellationToken cancellationToken)
    {
        using var stream = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var file in Files(dir))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = rootName + "/" + Path.GetRelativePath(dir, file.FullName).Replace('\\', '/');
            var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
            entry.LastWriteTime = file.LastWriteTime;
            using (var input = file.OpenRead())
            using (var output = entry.Open())
            {
                input.CopyTo(output);
            }

            copied(file.Length);
        }
    }

    public static void DeleteQuietly(string dir)
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
            // A leftover staging folder costs disk space, not data.
        }
    }

    /// <summary>A folder or file name that is safe to show in Explorer and Minecraft's world list.</summary>
    public static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        var cleaned = new string(name.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        return cleaned.Length == 0 || cleaned is "." or ".." ? "world" : cleaned;
    }

    private static ZipArchive OpenZip(string zipPath)
    {
        try
        {
            return ZipFile.OpenRead(zipPath);
        }
        catch (InvalidDataException e)
        {
            throw new WorldException(WorldProblem.DamagedZip, e.Message);
        }
    }

    private static string Normalize(string entryName) => entryName.Replace('\\', '/');

    // Minecraft holds session.lock open while running and writes a new one on every start, so it is never worth copying.
    private static bool IsSessionLock(string fileName) => fileName.Equals("session.lock", StringComparison.OrdinalIgnoreCase);
}
