using System.IO.Compression;
using System.Security.Cryptography;

namespace WaNut.Core.Packs;

/// <summary>
/// Combines several resource pack zips into one, because server.properties offers only one pack. Packs earlier in the
/// list have priority: when two packs hold the same file, the earlier one's copy is kept, like the order in Minecraft's
/// own pack screen. The merged pack uses the first pack's pack.mcmeta and pack.png.
/// </summary>
public static class ResourcePackMerger
{
    private const string McMeta = "pack.mcmeta";
    private const string Icon = "pack.png";

    /// <summary>The same timestamp on every entry makes the same packs give the same zip, so the SHA-1 stays put.</summary>
    private static readonly DateTimeOffset FixedTime = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Writes the merged zip to <paramref name="outputZip"/> (via a temporary file) and reports what overlapped.</summary>
    /// <param name="packZips">Zip paths, highest priority first.</param>
    /// <exception cref="InvalidDataException">A file is not a zip, or has no pack.mcmeta at its top.</exception>
    /// <exception cref="ArgumentException">Fewer than one pack.</exception>
    public static ResourcePackMergeResult Merge(IReadOnlyList<string> packZips, string outputZip)
    {
        if (packZips.Count == 0)
        {
            throw new ArgumentException("Give at least one resource pack.", nameof(packZips));
        }

        // path (as written in the zip) -> (pack that supplied it, entry). Lookup ignores case: Windows games read paths that way.
        var chosen = new SortedDictionary<string, (string Pack, ZipArchiveEntry Entry)>(StringComparer.Ordinal);
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var losers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var winners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var archives = new List<ZipArchive>();
        try
        {
            foreach (var zipPath in packZips)
            {
                var name = Path.GetFileName(zipPath);
                ZipArchive archive;
                try
                {
                    archive = ZipFile.OpenRead(zipPath);
                }
                catch (InvalidDataException)
                {
                    throw new InvalidDataException($"{name} is not a zip file.");
                }

                archives.Add(archive);
                if (archive.GetEntry(McMeta) is null)
                {
                    throw new InvalidDataException($"{name} has no pack.mcmeta at its top, so it is not a resource pack.");
                }

                foreach (var entry in archive.Entries)
                {
                    var path = Normalize(entry.FullName);
                    if (path is null || entry.Name.Length == 0)
                    {
                        continue; // folders and unsafe paths
                    }

                    if (seen.TryGetValue(path, out var winner))
                    {
                        // pack.mcmeta and pack.png are meant to differ between packs, so they are not worth a warning.
                        if (path is not (McMeta or Icon))
                        {
                            winners[path] = winner;
                            if (!losers.TryGetValue(path, out var list))
                            {
                                losers[path] = list = [];
                            }

                            list.Add(name);
                        }

                        continue;
                    }

                    seen[path] = name;
                    chosen[path] = (name, entry);
                }
            }

            var temp = outputZip + ".tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputZip))!);
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.ReadWrite))
            {
                using var output = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
                foreach (var (path, (_, entry)) in chosen)
                {
                    var created = output.CreateEntry(path, CompressionLevel.Optimal);
                    created.LastWriteTime = FixedTime;
                    using var source = entry.Open();
                    using var target = created.Open();
                    source.CopyTo(target);
                }
            }

            string sha1;
            using (var read = File.OpenRead(temp))
            {
                sha1 = Convert.ToHexStringLower(SHA1.HashData(read));
            }

            File.Move(temp, outputZip, overwrite: true);
            var overlaps = losers
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new ResourcePackOverlap(pair.Key, winners[pair.Key], pair.Value))
                .ToList();
            return new ResourcePackMergeResult(sha1, chosen.Count, overlaps);
        }
        finally
        {
            foreach (var archive in archives)
            {
                archive.Dispose();
            }
        }
    }

    /// <summary>Forward slashes, no leading slash; null for a path that would leave the pack (zip slip) or is empty.</summary>
    private static string? Normalize(string fullName)
    {
        var path = fullName.Replace('\\', '/').TrimStart('/');
        if (path.Length == 0 || path.Contains(':') || path.Split('/').Any(part => part == ".."))
        {
            return null;
        }

        return path;
    }
}
