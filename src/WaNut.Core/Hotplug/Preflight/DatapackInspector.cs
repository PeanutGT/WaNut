using System.IO.Compression;
using System.Security.Cryptography;

namespace WaNut.Core.Hotplug.Preflight;

/// <summary>
/// The pre-flight check of a data pack zip on this PC, before anything goes to the VPS: it must be a zip with
/// pack.mcmeta at its top, its format is compared with the server's version, and its data folders are sorted by how
/// much of them /reload can apply (<see cref="PackRisk"/>).
/// </summary>
public static class DatapackInspector
{
    /// <summary>Adds dimensions: never swapped in while the server runs.</summary>
    private static readonly HashSet<string> RestartFolders = new(StringComparer.Ordinal) { "dimension", "dimension_type" };

    /// <summary>
    /// Read only when the world opens: world generation and the registries Minecraft keeps per world since 1.20.5.
    /// /reload leaves them as they were.
    /// </summary>
    private static readonly HashSet<string> WorldLoadFolders = new(StringComparer.Ordinal)
    {
        "worldgen", "enchantment", "enchantment_provider", "damage_type", "chat_type", "banner_pattern", "trim_material",
        "trim_pattern", "painting_variant", "wolf_variant", "wolf_sound_variant", "cat_variant", "cow_variant",
        "chicken_variant", "frog_variant", "pig_variant", "jukebox_song", "instrument", "trial_spawner", "dialog",
    };

    /// <param name="expectedFormat">The server's format from <see cref="DatapackFormats.For"/>; null skips the version check.</param>
    public static async Task<DatapackInspection> InspectAsync(string localPath, int? expectedFormat, CancellationToken cancellationToken = default)
    {
        var name = Path.GetFileName(localPath);
        var info = new FileInfo(localPath);
        var sha = await Sha256Async(localPath, cancellationToken).ConfigureAwait(false);
        var nameProblem = HotplugNames.Problem(name);
        if (nameProblem is not null)
        {
            return Refused(nameProblem);
        }

        try
        {
            await using var stream = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            var meta = zip.GetEntry(PackMetadata.FileName);
            if (meta is null)
            {
                var nested = zip.Entries.Any(e => e.FullName.EndsWith("/" + PackMetadata.FileName, StringComparison.Ordinal));
                return Refused(nested
                    ? "pack.mcmeta is inside a folder in the zip; Minecraft only reads it at the top. Zip the folder's contents instead."
                    : "The zip has no pack.mcmeta, so it isn't a data pack.");
            }

            string text;
            await using (var entry = meta.Open())
            using (var reader = new StreamReader(entry))
            {
                text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            }

            var metadata = PackMetadata.TryParse(text, out var problem);
            if (metadata is null)
            {
                return Refused(problem);
            }

            var folders = zip.Entries.Select(e => DataFolder(e.FullName)).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
            var restart = folders.Where(RestartFolders.Contains).Order(StringComparer.Ordinal).ToList();
            var later = folders.Where(WorldLoadFolders.Contains).Order(StringComparer.Ordinal).ToList();
            var (risk, risky) = restart.Count > 0 ? (PackRisk.NeedsRestart, restart)
                : later.Count > 0 ? (PackRisk.AppliesAfterRestart, later)
                : (PackRisk.Safe, new List<string>());
            return new DatapackInspection(localPath, name, info.Length, sha, metadata, risk, risky, DatapackFormats.Check(metadata, expectedFormat), expectedFormat, null);
        }
        catch (InvalidDataException)
        {
            return Refused("The file isn't a readable zip.");
        }

        DatapackInspection Refused(string? why) =>
            new(localPath, name, info.Length, sha, null, PackRisk.Safe, [], FormatCheck.Unknown, expectedFormat, why);
    }

    public static async Task<string> Sha256Async(string localPath, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>"data/ns/loot_table/x.json" (or the same under an overlay folder) gives "loot_table"; anything else null.</summary>
    public static string? DataFolder(string entryName)
    {
        var parts = entryName.Split('/');
        for (var i = 0; i <= 1 && i + 3 < parts.Length; i++)
        {
            if (parts[i] == "data" && parts[i + 1].Length > 0 && parts[i + 2].Length > 0)
            {
                return parts[i + 2];
            }
        }

        return null;
    }
}
