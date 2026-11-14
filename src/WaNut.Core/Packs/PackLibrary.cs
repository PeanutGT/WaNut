using System.Text.Json;
using WaNut.Core.Downloads;
using WaNut.Core.Instances;
using WaNut.Core.Mods;
using WaNut.Core.Properties;
using WaNut.Core.Settings;

namespace WaNut.Core.Packs;

/// <summary>
/// The server's data packs (zips or folders in the world's datapacks folder) and the resource pack server.properties
/// offers players on join, both from Modrinth. Works for every server type, Vanilla too.
/// </summary>
public sealed class PackLibrary(ModrinthClient modrinth, FileDownloader downloader)
{
    public const string RecordsFileName = "installed-packs.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public ModrinthClient Modrinth => modrinth;

    /// <summary>The world's datapacks folder. Throws when level-name is not a plain folder name.</summary>
    public static string DatapacksDir(string instanceDir)
    {
        var level = GameSettingsStore.LevelName(instanceDir);
        if (!IsPlainName(level))
        {
            throw new InvalidDataException($"The world name \"{level}\" in server.properties is not a plain folder name.");
        }

        return Path.Combine(instanceDir, level, "datapacks");
    }

    /// <summary>Every zip and folder in the datapacks folder; packs copied in by hand show by name.</summary>
    public IReadOnlyList<InstalledPack> ListDatapacks(string instanceDir)
    {
        var dir = DatapacksDir(instanceDir);
        if (!Directory.Exists(dir))
        {
            return [];
        }

        var records = ReadRecords(instanceDir).Datapacks.ToDictionary(r => r.FileName, StringComparer.OrdinalIgnoreCase);
        return Directory.EnumerateFiles(dir, "*.zip").Concat(Directory.EnumerateDirectories(dir))
            .Select(Path.GetFileName)
            .Select(name => records.GetValueOrDefault(name!) ?? new InstalledPack(name!))
            .OrderBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Downloads the pack's best version for this Minecraft version into the datapacks folder (made if the world is
    /// not there yet: a new world starts with it). Only call while the server is stopped.
    /// </summary>
    /// <returns>False when the server already has the project.</returns>
    /// <exception cref="NoCompatibleVersionException">The pack has no version for this Minecraft version.</exception>
    public async Task<bool> InstallDatapackAsync(
        string instanceDir, string gameVersion, string projectId, string title, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (ListDatapacks(instanceDir).Any(p => p.ProjectId == projectId))
        {
            return false;
        }

        var version = await modrinth.GetBestPackVersionAsync(projectId, PackKind.DataPack, gameVersion, cancellationToken).ConfigureAwait(false)
            ?? throw new NoCompatibleVersionException(title);
        var dir = DatapacksDir(instanceDir);
        Directory.CreateDirectory(dir);
        var fileProgress = progress is null ? null : new SyncProgress<DownloadProgress>(p => progress.Report(p.Fraction ?? 0));
        await downloader.DownloadAsync(version.Url, Path.Combine(dir, version.FileName), ExpectedHash.Sha512(version.Sha512), fileProgress, cancellationToken).ConfigureAwait(false);

        var records = ReadRecords(instanceDir);
        records.Datapacks.RemoveAll(r => string.Equals(r.FileName, version.FileName, StringComparison.OrdinalIgnoreCase));
        records.Datapacks.Add(new InstalledPack(version.FileName, projectId, title));
        WriteRecords(instanceDir, records);
        progress?.Report(1);
        return true;
    }

    /// <summary>Deletes the zip or folder. Only call while the server is stopped.</summary>
    public void RemoveDatapack(string instanceDir, InstalledPack pack)
    {
        if (!IsPlainName(pack.FileName))
        {
            throw new ArgumentException($"\"{pack.FileName}\" is not a pack in the datapacks folder.", nameof(pack));
        }

        var path = Path.Combine(DatapacksDir(instanceDir), pack.FileName);
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
        else
        {
            File.Delete(path);
        }

        var records = ReadRecords(instanceDir);
        records.Datapacks.RemoveAll(r => string.Equals(r.FileName, pack.FileName, StringComparison.OrdinalIgnoreCase));
        WriteRecords(instanceDir, records);
    }

    /// <summary>The resource pack server.properties offers, or null when it offers none.</summary>
    public ServerResourcePack? GetResourcePack(string instanceDir)
    {
        var properties = PropertiesFile.Load(PropertiesPath(instanceDir));
        var url = properties.Get("resource-pack");
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var record = ReadRecords(instanceDir).ResourcePack;
        var sha1 = properties.Get("resource-pack-sha1");
        return new ServerResourcePack(
            url,
            string.IsNullOrWhiteSpace(sha1) ? null : sha1,
            string.Equals(properties.Get("require-resource-pack"), "true", StringComparison.OrdinalIgnoreCase),
            record is not null && record.Url == url ? record.Title : null);
    }

    /// <summary>
    /// Points server.properties at the pack's best version on Modrinth's CDN with its SHA-1, so players' games
    /// download it on join. Nothing is downloaded here. Only call while the server is stopped: the server rewrites
    /// server.properties when it starts.
    /// </summary>
    /// <exception cref="NoCompatibleVersionException">The pack has no version for this Minecraft version.</exception>
    public async Task<ServerResourcePack> SetResourcePackAsync(
        string instanceDir, string gameVersion, string projectId, string title, bool required, CancellationToken cancellationToken = default)
    {
        var version = await modrinth.GetBestPackVersionAsync(projectId, PackKind.ResourcePack, gameVersion, cancellationToken).ConfigureAwait(false)
            ?? throw new NoCompatibleVersionException(title);
        var pack = new ServerResourcePack(version.Url.AbsoluteUri, version.Sha1, required, title);
        WriteResourcePack(instanceDir, pack);
        var records = ReadRecords(instanceDir);
        records.ResourcePack = new ResourcePackRecord(projectId, title, pack.Url);
        WriteRecords(instanceDir, records);
        return pack;
    }

    /// <summary>Changes only whether players must accept the pack.</summary>
    public void SetResourcePackRequired(string instanceDir, bool required)
    {
        var properties = PropertiesFile.Load(PropertiesPath(instanceDir));
        properties.Set("require-resource-pack", required ? "true" : "false");
        properties.Save(PropertiesPath(instanceDir));
    }

    /// <summary>Stops offering a resource pack.</summary>
    public void ClearResourcePack(string instanceDir)
    {
        WriteResourcePack(instanceDir, new ServerResourcePack(string.Empty, string.Empty, false));
        var records = ReadRecords(instanceDir);
        records.ResourcePack = null;
        WriteRecords(instanceDir, records);
    }

    /// <summary>The name comes from the internet and becomes a path: it must stay inside the folder and be a zip.</summary>
    public static bool IsSafeZipName(string name) =>
        name.Length > 4 && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && IsPlainName(name);

    private static bool IsPlainName(string name) =>
        name.Length > 0
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && name.IndexOfAny(['/', '\\', ':', '*', '?', '"', '<', '>', '|']) < 0
        && name != "." && name != ".."
        && !name.StartsWith('.')
        && name.Trim() == name;

    private static void WriteResourcePack(string instanceDir, ServerResourcePack pack)
    {
        var path = PropertiesPath(instanceDir);
        var properties = PropertiesFile.Load(path);
        properties.Set("resource-pack", pack.Url);
        properties.Set("resource-pack-sha1", pack.Sha1 ?? string.Empty);
        properties.Set("require-resource-pack", pack.Required ? "true" : "false");
        properties.Save(path);
    }

    private static string PropertiesPath(string instanceDir) => Path.Combine(instanceDir, GameSettingsStore.PropertiesFileName);

    private static PackRecords ReadRecords(string instanceDir)
    {
        var path = Path.Combine(instanceDir, RecordsFileName);
        if (!File.Exists(path))
        {
            return new PackRecords();
        }

        try
        {
            return JsonSerializer.Deserialize<PackRecords>(File.ReadAllText(path), JsonOptions) ?? new PackRecords();
        }
        catch (JsonException)
        {
            // Only names are lost: the packs still load.
            return new PackRecords();
        }
    }

    private static void WriteRecords(string instanceDir, PackRecords records) =>
        AtomicFile.WriteAllText(Path.Combine(instanceDir, RecordsFileName), JsonSerializer.Serialize(records, JsonOptions));

    private sealed class PackRecords
    {
        public List<InstalledPack> Datapacks { get; set; } = [];

        public ResourcePackRecord? ResourcePack { get; set; }
    }

    private sealed record ResourcePackRecord(string ProjectId, string Title, string Url);
}
