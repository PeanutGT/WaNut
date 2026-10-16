using System.Text.Encodings.Web;
using System.Text.Json;
using WaNut.Core.Downloads;
using WaNut.Core.Instances;

namespace WaNut.Core.Mods;

/// <summary>
/// The mods or plugins of one server: lists the jars in its folder, installs Modrinth projects with their required
/// dependencies (each file checked against its SHA-512), and removes files. Which jars came from Modrinth is kept in
/// installed-mods.json, so the list can show their names and search results can show them as installed.
/// </summary>
public sealed class ModLibrary(ModrinthClient modrinth, FileDownloader downloader)
{
    public const string RecordsFileName = "installed-mods.json";

    /// <summary>A safety stop for dependency chains; real ones are a few projects deep.</summary>
    private const int MaxFilesPerInstall = 20;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public ModrinthClient Modrinth => modrinth;

    /// <summary>
    /// Every jar in the folder, by name. Jars copied in by hand show up too; records whose file was deleted in
    /// Explorer are left out.
    /// </summary>
    public IReadOnlyList<InstalledMod> ListInstalled(string instanceDir, ModPlatform platform)
    {
        var folder = platform.FolderIn(instanceDir);
        if (!Directory.Exists(folder))
        {
            return [];
        }

        var records = ReadRecords(instanceDir).ToDictionary(r => r.FileName, StringComparer.OrdinalIgnoreCase);
        return Directory.EnumerateFiles(folder, "*.jar")
            .Select(Path.GetFileName)
            .Select(name => records.GetValueOrDefault(name!) ?? new InstalledMod(name!))
            .OrderBy(m => m.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Installs the project and the required dependencies the server does not have yet. Only call while the server
    /// is stopped: Windows locks the jars of a running server.
    /// </summary>
    /// <returns>Nothing installed when the server already has the project, even as a jar copied in by hand.</returns>
    /// <exception cref="NoCompatibleVersionException">The project itself has no version for this server.</exception>
    public async Task<ModInstallResult> InstallAsync(
        string instanceDir,
        ModPlatform platform,
        string gameVersion,
        string projectId,
        string title,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var installed = ListInstalled(instanceDir, platform);
        var known = installed.Where(m => m.ProjectId is not null).Select(m => m.ProjectId!).ToHashSet(StringComparer.Ordinal);

        // A dependency copied in by hand (often Fabric API) must not be downloaded a second time under another file
        // name: two copies of one mod stop the server from starting.
        var byHand = installed.Where(m => m.ProjectId is null).Select(m => HashFile(platform.FolderIn(instanceDir), m.FileName)).ToList();
        known.UnionWith(await modrinth.IdentifyProjectsAsync(byHand, cancellationToken).ConfigureAwait(false));

        // Work out every file first, so nothing is downloaded when the project itself cannot be installed.
        var plan = new List<(ModVersion Version, string Title)>();
        var missing = new List<string>();
        var queue = new Queue<(string ProjectId, string? Title)>();
        if (!known.Add(projectId))
        {
            return new ModInstallResult([], []);
        }

        queue.Enqueue((projectId, title));
        while (queue.TryDequeue(out var next) && plan.Count < MaxFilesPerInstall)
        {
            var version = await modrinth.GetBestVersionAsync(next.ProjectId, platform, gameVersion, cancellationToken).ConfigureAwait(false);
            var name = next.Title ?? await modrinth.GetProjectTitleAsync(next.ProjectId, cancellationToken).ConfigureAwait(false);
            if (version is null)
            {
                if (plan.Count == 0)
                {
                    throw new NoCompatibleVersionException(name);
                }

                missing.Add(name);
                continue;
            }

            plan.Add((version, name));
            foreach (var dependency in version.RequiredProjectIds.Where(known.Add))
            {
                queue.Enqueue((dependency, null));
            }
        }

        var folder = platform.FolderIn(instanceDir);
        Directory.CreateDirectory(folder);
        var records = ReadRecords(instanceDir).Where(r => File.Exists(Path.Combine(folder, r.FileName))).ToList();
        for (var i = 0; i < plan.Count; i++)
        {
            var (version, name) = plan[i];
            var step = i;
            var fileProgress = progress is null
                ? null
                : new SyncProgress<DownloadProgress>(p => progress.Report((step + (p.Fraction ?? 0)) / plan.Count));
            await downloader.DownloadAsync(
                version.Url,
                Path.Combine(folder, version.FileName),
                ExpectedHash.Sha512(version.Sha512),
                fileProgress,
                cancellationToken).ConfigureAwait(false);

            // Saved after every file, so a failure halfway still leaves the finished ones named.
            records.RemoveAll(r => string.Equals(r.FileName, version.FileName, StringComparison.OrdinalIgnoreCase));
            records.Add(new InstalledMod(version.FileName, version.ProjectId, version.VersionId, name));
            WriteRecords(instanceDir, records);
        }

        progress?.Report(1);
        return new ModInstallResult(plan.Select(p => p.Title).ToList(), missing);
    }

    /// <summary>
    /// Gives the server its own copies of a single-player instance's mods (decision 24, "Install the mods too"). Each
    /// jar is looked up on Modrinth by its SHA-512; mods that only run in the game are skipped, and the rest are
    /// installed in their version for this server, with required dependencies. Jars Modrinth does not know are not
    /// copied: WaNut cannot tell whether they run on a server, and a client-only mod stops the server from starting.
    /// Only call while the server is stopped.
    /// </summary>
    public async Task<ModCopyResult> InstallCopiesAsync(
        string instanceDir,
        ModPlatform platform,
        string gameVersion,
        IReadOnlyList<string> jarPaths,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // The same jar twice (two copies in one folder) counts once.
        var fileNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in jarPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            fileNames.TryAdd(HashFile(path), Path.GetFileName(path));
        }

        var projectByHash = await modrinth.IdentifyFilesAsync(fileNames.Keys, cancellationToken).ConfigureAwait(false);
        var projects = await modrinth.GetProjectsAsync(projectByHash.Values.ToHashSet(StringComparer.Ordinal), cancellationToken).ConfigureAwait(false);
        var found = projects.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var unknown = fileNames
            .Where(f => !projectByHash.TryGetValue(f.Key, out var id) || !found.Contains(id))
            .Select(f => f.Value)
            .Order(StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var clientOnly = projects.Where(p => p.IsClientOnly).Select(p => p.Title).Order(StringComparer.CurrentCultureIgnoreCase).ToList();
        var wanted = projects.Where(p => !p.IsClientOnly).OrderBy(p => p.Title, StringComparer.CurrentCultureIgnoreCase).ToList();

        var installed = new List<string>();
        var noVersion = new List<string>();
        for (var i = 0; i < wanted.Count; i++)
        {
            var step = i;
            var projectProgress = progress is null ? null : new SyncProgress<double>(f => progress.Report((step + f) / wanted.Count));
            try
            {
                var result = await InstallAsync(instanceDir, platform, gameVersion, wanted[i].Id, wanted[i].Title, projectProgress, cancellationToken).ConfigureAwait(false);
                installed.AddRange(result.Installed);
                noVersion.AddRange(result.MissingDependencies);
            }
            catch (NoCompatibleVersionException)
            {
                noVersion.Add(wanted[i].Title);
            }
        }

        progress?.Report(1);
        return new ModCopyResult(installed, noVersion.Distinct().ToList(), clientOnly, unknown);
    }

    /// <summary>Deletes the jar. Only call while the server is stopped.</summary>
    public void Remove(string instanceDir, ModPlatform platform, InstalledMod mod)
    {
        if (!ModrinthClient.IsSafeJarName(mod.FileName))
        {
            throw new ArgumentException($"\"{mod.FileName}\" is not a jar in the {platform.FolderName} folder.", nameof(mod));
        }

        File.Delete(Path.Combine(platform.FolderIn(instanceDir), mod.FileName));
        var records = ReadRecords(instanceDir)
            .Where(r => !string.Equals(r.FileName, mod.FileName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        WriteRecords(instanceDir, records);
    }

    private static string HashFile(string folder, string fileName) => HashFile(Path.Combine(folder, fileName));

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA512.HashData(stream));
    }

    private static List<InstalledMod> ReadRecords(string instanceDir)
    {
        var path = Path.Combine(instanceDir, RecordsFileName);
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<InstalledMod>>(File.ReadAllText(path), JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            // Only names are lost: the jars still load, and they show by file name until reinstalled.
            return [];
        }
    }

    private static void WriteRecords(string instanceDir, List<InstalledMod> records) =>
        AtomicFile.WriteAllText(Path.Combine(instanceDir, RecordsFileName), JsonSerializer.Serialize(records, JsonOptions));
}
