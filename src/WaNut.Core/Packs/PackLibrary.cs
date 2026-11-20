using System.IO.Compression;
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
public sealed class PackLibrary(ModrinthClient modrinth, FileDownloader downloader, HttpClient? http = null)
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
        foreach (var pack in records.ResourcePacks.Where(p => IsSafeZipName(p.FileName)))
        {
            File.Delete(Path.Combine(ResourcePacksDir(instanceDir), pack.FileName));
        }

        records.ResourcePacks.Clear();
        records.CustomPackUrl = null;
        File.Delete(MergedPackPath(instanceDir));
        WriteRecords(instanceDir, records);
    }

    /// <summary>Whether server.properties makes players accept the resource pack.</summary>
    public static bool IsResourcePackRequired(string instanceDir) =>
        string.Equals(PropertiesFile.Load(PropertiesPath(instanceDir)).Get("require-resource-pack"), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>The zip WaNut serves when two or more resource packs are merged.</summary>
    public const string MergedFileName = "resourcepack-merged.zip";

    public static string ResourcePacksDir(string instanceDir) => Path.Combine(instanceDir, "resourcepacks");

    public static string MergedPackPath(string instanceDir) => Path.Combine(instanceDir, MergedFileName);

    /// <summary>The server's resource packs, highest priority first.</summary>
    public IReadOnlyList<ResourcePackEntry> ListResourcePacks(string instanceDir) => [.. ReadRecords(instanceDir).ResourcePacks];

    /// <summary>
    /// Downloads the pack's best version and adds it at the bottom of the list (lowest priority). One pack is offered
    /// from Modrinth as before; two or more are merged into one zip WaNut serves while the server runs. A pack set by
    /// the older single-pack feature joins the list first. Only call while the server is stopped.
    /// </summary>
    /// <exception cref="NoCompatibleVersionException">The pack has no version for this Minecraft version.</exception>
    /// <exception cref="InvalidDataException">A pack zip cannot be merged (see <see cref="ResourcePackMerger"/>).</exception>
    public async Task<ResourcePackChange> AddResourcePackAsync(
        string instanceDir, string gameVersion, string projectId, string title, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var records = ReadRecords(instanceDir);
        if (records.ResourcePacks.Any(p => p.ProjectId == projectId))
        {
            return new ResourcePackChange(false, []);
        }

        if (records.ResourcePacks.Count == 0 && records.ResourcePack is { } old && old.ProjectId != projectId)
        {
            records.ResourcePacks.Add(await DownloadResourcePackAsync(instanceDir, gameVersion, old.ProjectId, old.Title, null, cancellationToken).ConfigureAwait(false));
        }

        records.ResourcePacks.Add(await DownloadResourcePackAsync(instanceDir, gameVersion, projectId, title, progress, cancellationToken).ConfigureAwait(false));
        return ApplyResourcePacks(instanceDir, records);
    }

    /// <summary>
    /// Adds a resource pack zip from this PC at the bottom of the list (lowest priority): the zip is copied into the
    /// server's resourcepacks folder. A pack with no Modrinth address always goes out as WaNut's merged zip, even
    /// alone, so players download it from WaNut's sharing or the user's own link. The same file added twice is
    /// ignored. Only call while the server is stopped.
    /// </summary>
    /// <exception cref="InvalidDataException">The file is not a zip, or has no pack.mcmeta at its top.</exception>
    /// <exception cref="NoCompatibleVersionException">An older single Modrinth pack that has to join has no version for this Minecraft version.</exception>
    public async Task<ResourcePackChange> AddLocalResourcePackAsync(
        string instanceDir, string gameVersion, string zipPath, CancellationToken cancellationToken = default)
    {
        var sha1 = ResourcePackLink.Sha1Of(zipPath);
        var records = ReadRecords(instanceDir);
        var projectId = LocalProjectPrefix + sha1[..12];
        if (records.ResourcePacks.Any(p => p.ProjectId == projectId))
        {
            return new ResourcePackChange(false, []);
        }

        var name = Path.GetFileName(zipPath);
        CheckIsResourcePack(zipPath, name);
        var title = Path.GetFileNameWithoutExtension(name);
        var fileName = LocalFileName(projectId, name);
        Directory.CreateDirectory(ResourcePacksDir(instanceDir));
        File.Copy(zipPath, Path.Combine(ResourcePacksDir(instanceDir), fileName), overwrite: true);

        if (records.ResourcePacks.Count == 0 && records.ResourcePack is { } old)
        {
            records.ResourcePacks.Add(await DownloadResourcePackAsync(instanceDir, gameVersion, old.ProjectId, old.Title, null, cancellationToken).ConfigureAwait(false));
        }

        records.ResourcePacks.Add(new ResourcePackEntry(fileName, projectId, title, string.Empty, sha1));
        return ApplyResourcePacks(instanceDir, records);
    }

    /// <summary>Whether this pack has no Modrinth address, so it can only be offered inside WaNut's own zip.</summary>
    public static bool IsLocalPack(ResourcePackEntry pack) => pack.Url.Length == 0;

    private const string LocalProjectPrefix = "local-";

    private static string LocalFileName(string projectId, string name)
    {
        var clean = new string([.. name.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c is ':' or '*' or '?' or '"' or '<' or '>' or '|' or '/' or '\\' ? '_' : c)]).TrimStart('.').Trim();
        if (!clean.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            clean += ".zip";
        }

        return $"{projectId}-{clean}";
    }

    private static void CheckIsResourcePack(string zipPath, string name)
    {
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            if (archive.GetEntry("pack.mcmeta") is null)
            {
                throw new InvalidDataException($"{name} has no pack.mcmeta at its top, so it is not a resource pack.");
            }
        }
        catch (InvalidDataException e) when (!e.Message.Contains("pack.mcmeta", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"{name} is not a zip file.", e);
        }
    }

    /// <summary>Moves a pack to <paramref name="newIndex"/>; index 0 has the top priority.</summary>
    public ResourcePackChange MoveResourcePack(string instanceDir, string fileName, int newIndex)
    {
        var records = ReadRecords(instanceDir);
        var from = records.ResourcePacks.FindIndex(p => p.FileName == fileName);
        var to = Math.Clamp(newIndex, 0, Math.Max(records.ResourcePacks.Count - 1, 0));
        if (from < 0 || from == to)
        {
            return new ResourcePackChange(false, []);
        }

        var pack = records.ResourcePacks[from];
        records.ResourcePacks.RemoveAt(from);
        records.ResourcePacks.Insert(to, pack);
        return ApplyResourcePacks(instanceDir, records);
    }

    /// <summary>Takes a pack out of the list and deletes its zip.</summary>
    public ResourcePackChange RemoveResourcePack(string instanceDir, string fileName)
    {
        var records = ReadRecords(instanceDir);
        if (records.ResourcePacks.RemoveAll(p => p.FileName == fileName) == 0)
        {
            return new ResourcePackChange(false, []);
        }

        if (IsSafeZipName(fileName))
        {
            File.Delete(Path.Combine(ResourcePacksDir(instanceDir), fileName));
        }

        return ApplyResourcePacks(instanceDir, records);
    }

    private static bool NeedsMergedZip(List<ResourcePackEntry> packs) => packs.Count >= 2 || packs.Exists(IsLocalPack);

    /// <summary>The merged zip when two or more packs are merged or the only pack is one from this PC, else null.</summary>
    public string? GetMergedPackPath(string instanceDir) =>
        NeedsMergedZip(ReadRecords(instanceDir).ResourcePacks) && File.Exists(MergedPackPath(instanceDir)) ? MergedPackPath(instanceDir) : null;

    /// <summary>The public address the user uploaded the merged pack to, or null when WaNut serves it itself.</summary>
    public Uri? GetCustomPackUrl(string instanceDir) =>
        ReadRecords(instanceDir).CustomPackUrl is { } text ? ResourcePackLink.Parse(text) : null;

    /// <summary>Chooses where players download the merged pack: <paramref name="url"/>, or null for WaNut's own sharing.</summary>
    public void SetCustomPackUrl(string instanceDir, Uri? url)
    {
        var records = ReadRecords(instanceDir);
        records.CustomPackUrl = url?.AbsoluteUri;
        WriteRecords(instanceDir, records);
    }

    /// <summary>Downloads the user's own link and compares it with the merged zip, so a stale upload or a share page is noticed.</summary>
    /// <exception cref="InvalidOperationException">No link is saved, there is no merged pack, or this library has no HTTP client.</exception>
    public Task<ResourcePackLinkCheck> CheckCustomPackUrlAsync(string instanceDir, CancellationToken cancellationToken = default)
    {
        if (http is null || GetCustomPackUrl(instanceDir) is not { } url || GetMergedPackPath(instanceDir) is not { } zip)
        {
            throw new InvalidOperationException("There is no link and merged pack to check.");
        }

        return ResourcePackLink.CheckAsync(http, url, ResourcePackLink.Sha1Of(zip), cancellationToken);
    }

    /// <summary>Points server.properties at where the merged pack is served now. Only call while the server is stopped.</summary>
    public void PointResourcePackAt(string instanceDir, Uri url)
    {
        var properties = PropertiesFile.Load(PropertiesPath(instanceDir));
        properties.Set("resource-pack", url.AbsoluteUri);
        properties.Save(PropertiesPath(instanceDir));
    }

    /// <summary>
    /// Points a server.properties at the merged pack for a VPS server: the address, the zip's SHA-1 and whether it is
    /// required. All three are written because the VPS's own copy of the file may be older than this PC's records.
    /// </summary>
    /// <exception cref="FileNotFoundException">There is no merged pack.</exception>
    public static void PointMergedPackAt(string propertiesPath, string mergedZipPath, Uri url, bool required)
    {
        var properties = PropertiesFile.Load(propertiesPath);
        properties.Set("resource-pack", url.AbsoluteUri);
        properties.Set("resource-pack-sha1", ResourcePackLink.Sha1Of(mergedZipPath));
        properties.Set("require-resource-pack", required ? "true" : "false");
        properties.Save(propertiesPath);
    }

    private async Task<ResourcePackEntry> DownloadResourcePackAsync(
        string instanceDir, string gameVersion, string projectId, string title, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var version = await modrinth.GetBestPackVersionAsync(projectId, PackKind.ResourcePack, gameVersion, cancellationToken).ConfigureAwait(false)
            ?? throw new NoCompatibleVersionException(title);
        if (!IsSafeZipName(version.FileName))
        {
            throw new InvalidDataException($"\"{version.FileName}\" is not a safe file name.");
        }

        // The project id in front keeps two projects that ship the same file name apart.
        var fileName = $"{projectId}-{version.FileName}";
        Directory.CreateDirectory(ResourcePacksDir(instanceDir));
        var fileProgress = progress is null ? null : new SyncProgress<DownloadProgress>(p => progress.Report(p.Fraction ?? 0));
        await downloader.DownloadAsync(version.Url, Path.Combine(ResourcePacksDir(instanceDir), fileName), ExpectedHash.Sha512(version.Sha512), fileProgress, cancellationToken).ConfigureAwait(false);
        progress?.Report(1);
        return new ResourcePackEntry(fileName, projectId, title, version.Url.AbsoluteUri, version.Sha1);
    }

    /// <summary>Writes server.properties and the records to match the list: none, one from Modrinth, or merged.</summary>
    private static ResourcePackChange ApplyResourcePacks(string instanceDir, PackRecords records)
    {
        var properties = PropertiesFile.Load(PropertiesPath(instanceDir));
        var required = string.Equals(properties.Get("require-resource-pack"), "true", StringComparison.OrdinalIgnoreCase);
        var packs = records.ResourcePacks;
        IReadOnlyList<ResourcePackOverlap> overlaps = [];
        records.ResourcePack = null;
        switch (NeedsMergedZip(packs) ? 2 : packs.Count)
        {
            case 0:
                WriteResourcePack(instanceDir, new ServerResourcePack(string.Empty, string.Empty, false));
                File.Delete(MergedPackPath(instanceDir));
                break;
            case 1:
                WriteResourcePack(instanceDir, new ServerResourcePack(packs[0].Url, packs[0].Sha1, required, packs[0].Title));
                records.ResourcePack = new ResourcePackRecord(packs[0].ProjectId, packs[0].Title, packs[0].Url);
                File.Delete(MergedPackPath(instanceDir));
                break;
            default:
                var merge = ResourcePackMerger.Merge([.. packs.Select(p => Path.Combine(ResourcePacksDir(instanceDir), p.FileName))], MergedPackPath(instanceDir));
                overlaps = merge.Overlaps;
                // The address players use is only known when the server starts: see PointResourcePackAt.
                WriteResourcePack(instanceDir, new ServerResourcePack(string.Empty, merge.Sha1, required));
                break;
        }

        WriteRecords(instanceDir, records);
        return new ResourcePackChange(true, overlaps);
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

        /// <summary>The resource packs in priority order, first wins.</summary>
        public List<ResourcePackEntry> ResourcePacks { get; set; } = [];

        /// <summary>Where the user hosts the merged pack; null means WaNut serves it while the server runs.</summary>
        public string? CustomPackUrl { get; set; }
    }

    private sealed record ResourcePackRecord(string ProjectId, string Title, string Url);
}
