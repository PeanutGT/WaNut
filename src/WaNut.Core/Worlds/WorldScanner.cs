using System.IO.Compression;

namespace WaNut.Core.Worlds;

/// <summary>Lists the single-player worlds of every launcher, and reads a folder or zip the user picked.</summary>
public static class WorldScanner
{
    /// <summary>
    /// The worlds in each location's saves folder, most recently played first. Folders without a readable
    /// level.dat are skipped.
    /// </summary>
    public static IReadOnlyList<WorldSource> Scan(IEnumerable<SaveLocation> locations, CancellationToken cancellationToken = default)
    {
        var worlds = new List<WorldSource>();
        foreach (var location in locations)
        {
            IReadOnlyList<string>? mods = null;
            foreach (var dir in Folders(location.SavesDir))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var details = TryRead(dir);
                if (details is null)
                {
                    continue;
                }

                // Only a modded world needs its instance's mod list, and reading it once per instance is enough.
                mods = details.IsModded ? mods ?? location.ModFiles() : mods;
                worlds.Add(new WorldSource(dir, null, details, location, details.IsModded ? mods! : []));
            }
        }

        return worlds.OrderByDescending(w => w.Details.LastPlayed ?? DateTimeOffset.MinValue).ToList();
    }

    /// <summary>A world folder the user picked: the folder itself holds level.dat.</summary>
    /// <exception cref="WorldException">NotAWorld when there is no readable level.dat.</exception>
    public static WorldSource FromFolder(string dir) =>
        new(dir, null, TryRead(dir) ?? throw new WorldException(WorldProblem.NotAWorld, dir), null, []);

    /// <summary>
    /// A zip the user picked. level.dat may be at the top or one or two folders down, as when a world folder was
    /// zipped with the folder around it.
    /// </summary>
    /// <exception cref="WorldException">DamagedZip when the zip can't be read; NotAWorld when it holds no world.</exception>
    public static WorldSource FromZip(string zipPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var levelDat = archive.Entries
                .Select(e => (Entry: e, Name: e.FullName.Replace('\\', '/')))
                .Where(e => e.Name.Split('/') is var parts && parts[^1] == LevelData.FileName && parts.Length <= 3 && !parts[0].StartsWith("__MACOSX", StringComparison.Ordinal))
                .OrderBy(e => e.Name.Length)
                .FirstOrDefault();
            if (levelDat.Entry is null)
            {
                throw new WorldException(WorldProblem.NotAWorld, zipPath);
            }

            var root = levelDat.Name[..^LevelData.FileName.Length];
            var fallback = root.Length > 0 ? root.TrimEnd('/').Split('/')[^1] : Path.GetFileNameWithoutExtension(zipPath);
            using var stream = levelDat.Entry.Open();
            return new WorldSource(zipPath, root, LevelData.ReadDetails(stream, fallback), null, []);
        }
        catch (InvalidDataException e)
        {
            throw new WorldException(WorldProblem.DamagedZip, e.Message);
        }
    }

    private static WorldDetails? TryRead(string dir)
    {
        var file = Path.Combine(dir, LevelData.FileName);
        try
        {
            return File.Exists(file) ? LevelData.ReadDetails(file, Path.GetFileName(dir)) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }

    private static IEnumerable<string> Folders(string dir)
    {
        try
        {
            return Directory.Exists(dir) ? Directory.EnumerateDirectories(dir).ToList() : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
