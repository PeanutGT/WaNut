using System.Text.Encodings.Web;
using System.Text.Json;
using WaNut.Core.Downloads;
using WaNut.Core.Instances;
using WaNut.Core.Storage;

namespace WaNut.Core.Mods;

/// <summary>
/// The mods or plugins of one server: lists the jars in its folder, installs Modrinth projects with their required
/// dependencies (each file checked against its SHA-512), and removes files. Which jars came from Modrinth is kept in
/// installed-mods.json, so the list can show their names and search results can show them as installed.
/// Each change works on a folder on this PC (an instance folder path) or straight on the VPS (an
/// <see cref="IServerFileSystem"/>), where a jar changed while the server runs loads on its next start.
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

        return Named(Directory.EnumerateFiles(folder, "*.jar").Select(p => Path.GetFileName(p)), ParseRecords(ReadLocalRecords(instanceDir)));
    }

    /// <summary>The same list as <see cref="ListInstalled(string, ModPlatform)"/>, read from <paramref name="files"/>.</summary>
    public Task<IReadOnlyList<InstalledMod>> ListInstalledAsync(IServerFileSystem files, ModPlatform platform, CancellationToken cancellationToken = default) =>
        ListInstalledAsync(new ServerModFiles(files), platform, cancellationToken);

    private static async Task<IReadOnlyList<InstalledMod>> ListInstalledAsync(IModFiles files, ModPlatform platform, CancellationToken cancellationToken)
    {
        var names = await files.ListJarsAsync(platform.FolderName, cancellationToken).ConfigureAwait(false);
        return Named(names, await ReadRecordsAsync(files, cancellationToken).ConfigureAwait(false));
    }

    private static List<InstalledMod> Named(IEnumerable<string> jarNames, List<InstalledMod> records)
    {
        var byName = new Dictionary<string, InstalledMod>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
        {
            byName.TryAdd(record.FileName, record);
        }

        return jarNames
            .Select(name => byName.GetValueOrDefault(name) ?? new InstalledMod(name))
            .OrderBy(m => m.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Installs the project and the required dependencies the server does not have yet. Only call while the server
    /// is stopped: Windows locks the jars of a running server.
    /// </summary>
    /// <param name="allowIncompatible">
    /// False checks first whether Modrinth marks anything to install and an installed mod as not working together
    /// (either side can say so). True skips that check, for a second try after the user said to go ahead.
    /// </param>
    /// <param name="chosenVersion">A version picked in the details window; null installs the best one for the server.</param>
    /// <returns>Nothing installed when the server already has the project, even as a jar copied in by hand.</returns>
    /// <exception cref="NoCompatibleVersionException">The project itself has no version for this server.</exception>
    /// <exception cref="IncompatibleModsException">Something to install clashes with an installed mod.</exception>
    public Task<ModInstallResult> InstallAsync(
        string instanceDir,
        ModPlatform platform,
        string gameVersion,
        string projectId,
        string title,
        bool allowIncompatible = false,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default,
        ModVersion? chosenVersion = null) =>
        InstallAsync(new LocalModFiles(instanceDir), platform, gameVersion, projectId, title, allowIncompatible, progress, chosenVersion, cancellationToken);

    /// <summary>The same as <see cref="InstallAsync(string, ModPlatform, string, string, string, bool, IProgress{double}?, CancellationToken, ModVersion?)"/>, in <paramref name="files"/>.</summary>
    public Task<ModInstallResult> InstallAsync(
        IServerFileSystem files,
        ModPlatform platform,
        string gameVersion,
        string projectId,
        string title,
        bool allowIncompatible = false,
        IProgress<double>? progress = null,
        ModVersion? chosenVersion = null,
        CancellationToken cancellationToken = default) =>
        InstallAsync(new ServerModFiles(files), platform, gameVersion, projectId, title, allowIncompatible, progress, chosenVersion, cancellationToken);

    private async Task<ModInstallResult> InstallAsync(
        IModFiles files,
        ModPlatform platform,
        string gameVersion,
        string projectId,
        string title,
        bool allowIncompatible,
        IProgress<double>? progress,
        ModVersion? chosenVersion,
        CancellationToken cancellationToken)
    {
        var folder = platform.FolderName;
        var installed = await ListInstalledAsync(files, platform, cancellationToken).ConfigureAwait(false);
        var known = installed.Where(m => m.ProjectId is not null).Select(m => m.ProjectId!).ToHashSet(StringComparer.Ordinal);

        // A dependency copied in by hand (often Fabric API) must not be downloaded a second time under another file
        // name: two copies of one mod stop the server from starting. The incompatibility check needs every jar's
        // version, so then all of them are looked up.
        var lookUp = new Dictionary<string, InstalledMod>(StringComparer.OrdinalIgnoreCase);
        var toHash = installed.Where(m => !allowIncompatible || m.ProjectId is null).ToList();
        var hashes = await files.HashAsync(toHash.Select(m => $"{folder}/{m.FileName}").ToList(), cancellationToken).ConfigureAwait(false);
        for (var i = 0; i < toHash.Count; i++)
        {
            lookUp.TryAdd(hashes[i], toHash[i]);
        }

        var identified = await modrinth.IdentifyVersionsAsync(lookUp.Keys, cancellationToken).ConfigureAwait(false);
        var installedNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var refusedBy = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (hash, version) in identified)
        {
            var name = lookUp[hash].DisplayName;
            known.Add(version.ProjectId);
            installedNames.TryAdd(version.ProjectId, name);
            foreach (var other in version.IncompatibleProjectIds)
            {
                (refusedBy.TryGetValue(other, out var names) ? names : refusedBy[other] = []).Add(name);
            }
        }

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
            var chosen = plan.Count == 0 && chosenVersion is not null
                ? chosenVersion
                : await modrinth.GetBestVersionAsync(next.ProjectId, platform, gameVersion, cancellationToken).ConfigureAwait(false);
            var name = next.Title ?? await modrinth.GetProjectTitleAsync(next.ProjectId, cancellationToken).ConfigureAwait(false);
            if (chosen is null)
            {
                if (plan.Count == 0)
                {
                    throw new NoCompatibleVersionException(name);
                }

                missing.Add(name);
                continue;
            }

            plan.Add((chosen, name));
            foreach (var dependency in chosen.RequiredProjectIds.Where(known.Add))
            {
                queue.Enqueue((dependency, null));
            }
        }

        if (!allowIncompatible)
        {
            var clashes = plan
                .SelectMany(p => p.Version.IncompatibleProjectIds.Where(installedNames.ContainsKey).Select(id => installedNames[id])
                    .Concat(refusedBy.GetValueOrDefault(p.Version.ProjectId) ?? []))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            if (clashes.Count > 0)
            {
                throw new IncompatibleModsException(title, clashes);
            }
        }

        var present = installed.Select(m => m.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var records = (await ReadRecordsAsync(files, cancellationToken).ConfigureAwait(false)).Where(r => present.Contains(r.FileName)).ToList();
        for (var i = 0; i < plan.Count; i++)
        {
            var (version, name) = plan[i];
            var step = i;
            var fileProgress = progress is null
                ? null
                : new SyncProgress<DownloadProgress>(p => progress.Report((step + (p.Fraction ?? 0)) / plan.Count));
            await files.ReceiveAsync(
                JarPath(folder, version.FileName),
                local => downloader.DownloadAsync(version.Url, local, ExpectedHash.Sha512(version.Sha512), fileProgress, cancellationToken),
                cancellationToken).ConfigureAwait(false);

            // Saved after every file, so a failure halfway still leaves the finished ones named.
            records.RemoveAll(r => string.Equals(r.FileName, version.FileName, StringComparison.OrdinalIgnoreCase));
            records.Add(new InstalledMod(version.FileName, version.ProjectId, version.VersionId, name));
            await WriteRecordsAsync(files, records, cancellationToken).ConfigureAwait(false);
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
                // These mods already ran together in the game, so Modrinth's incompatibility marks are not checked.
                var result = await InstallAsync(instanceDir, platform, gameVersion, wanted[i].Id, wanted[i].Title, allowIncompatible: true, projectProgress, cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// The installed jars, WaNut's or copied in by hand, that have a newer release on Modrinth for this loader and
    /// Minecraft version. Jars Modrinth does not know are left out.
    /// </summary>
    public Task<IReadOnlyList<ModUpdate>> CheckUpdatesAsync(
        string instanceDir, ModPlatform platform, string gameVersion, CancellationToken cancellationToken = default) =>
        CheckUpdatesAsync(new LocalModFiles(instanceDir), platform, gameVersion, cancellationToken);

    /// <summary>The same as <see cref="CheckUpdatesAsync(string, ModPlatform, string, CancellationToken)"/>; a VPS hashes its jars itself.</summary>
    public Task<IReadOnlyList<ModUpdate>> CheckUpdatesAsync(
        IServerFileSystem files, ModPlatform platform, string gameVersion, CancellationToken cancellationToken = default) =>
        CheckUpdatesAsync(new ServerModFiles(files), platform, gameVersion, cancellationToken);

    private async Task<IReadOnlyList<ModUpdate>> CheckUpdatesAsync(
        IModFiles files, ModPlatform platform, string gameVersion, CancellationToken cancellationToken)
    {
        var installed = await ListInstalledAsync(files, platform, cancellationToken).ConfigureAwait(false);
        var hashes = await files.HashAsync(installed.Select(m => $"{platform.FolderName}/{m.FileName}").ToList(), cancellationToken).ConfigureAwait(false);
        var byHash = new Dictionary<string, InstalledMod>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < installed.Count; i++)
        {
            byHash.TryAdd(hashes[i], installed[i]);
        }

        var latest = await modrinth.GetLatestVersionsAsync(byHash.Keys, platform, gameVersion, cancellationToken).ConfigureAwait(false);
        var newer = latest
            .Where(l => !string.Equals(l.Key, l.Value.Sha512, StringComparison.OrdinalIgnoreCase)
                && (byHash[l.Key].VersionId is not { } current || current != l.Value.VersionId))
            .ToList();
        if (newer.Count == 0)
        {
            return [];
        }

        var titles = (await modrinth.GetProjectsAsync(newer.Select(n => n.Value.ProjectId).Distinct(StringComparer.Ordinal).ToList(), cancellationToken).ConfigureAwait(false))
            .ToDictionary(p => p.Id, p => p.Title, StringComparer.Ordinal);
        return newer
            .Select(n => new ModUpdate(byHash[n.Key], n.Value, titles.GetValueOrDefault(n.Value.ProjectId) ?? byHash[n.Key].DisplayName))
            .OrderBy(u => u.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Puts each update's new jar in place of the old one. The new file is downloaded and checked before the old one
    /// goes, so a failed download leaves the mod as it was. Only call while the server is stopped.
    /// </summary>
    /// <returns>The titles of the mods that were updated, in order.</returns>
    public Task<IReadOnlyList<string>> UpdateAsync(
        string instanceDir,
        ModPlatform platform,
        IReadOnlyList<ModUpdate> updates,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(new LocalModFiles(instanceDir), platform, updates, progress, cancellationToken);

    /// <summary>The same as <see cref="UpdateAsync(string, ModPlatform, IReadOnlyList{ModUpdate}, IProgress{double}?, CancellationToken)"/>, in <paramref name="files"/>.</summary>
    public Task<IReadOnlyList<string>> UpdateAsync(
        IServerFileSystem files,
        ModPlatform platform,
        IReadOnlyList<ModUpdate> updates,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(new ServerModFiles(files), platform, updates, progress, cancellationToken);

    private async Task<IReadOnlyList<string>> UpdateAsync(
        IModFiles files,
        ModPlatform platform,
        IReadOnlyList<ModUpdate> updates,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var folder = platform.FolderName;
        var done = new List<string>();
        for (var i = 0; i < updates.Count; i++)
        {
            var (mod, version, title) = updates[i];
            if (!ModrinthClient.IsSafeJarName(mod.FileName))
            {
                throw new ArgumentException($"\"{mod.FileName}\" is not a jar in the {platform.FolderName} folder.", nameof(updates));
            }

            var step = i;
            var fileProgress = progress is null
                ? null
                : new SyncProgress<DownloadProgress>(p => progress.Report((step + (p.Fraction ?? 0)) / updates.Count));
            var sameName = string.Equals(mod.FileName, version.FileName, StringComparison.OrdinalIgnoreCase);
            await files.ReceiveAsync(
                JarPath(folder, version.FileName),
                local => downloader.DownloadAsync(version.Url, local, ExpectedHash.Sha512(version.Sha512), fileProgress, cancellationToken),
                cancellationToken).ConfigureAwait(false);
            if (!sameName)
            {
                await files.DeleteAsync(JarPath(folder, mod.FileName), cancellationToken).ConfigureAwait(false);
            }

            var records = (await ReadRecordsAsync(files, cancellationToken).ConfigureAwait(false))
                .Where(r => !string.Equals(r.FileName, mod.FileName, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(r.FileName, version.FileName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            records.Add(new InstalledMod(version.FileName, version.ProjectId, version.VersionId, title));
            await WriteRecordsAsync(files, records, cancellationToken).ConfigureAwait(false);
            done.Add(title);
        }

        progress?.Report(1);
        return done;
    }

    /// <summary>
    /// Copies jars the user dropped on the Mods tab into the folder and looks each up on Modrinth by SHA-512, so
    /// known ones show their project's name. A jar of a project the server already has, or with a file name already
    /// in the folder, is skipped: two copies of one mod stop the server. Only call while the server is stopped.
    /// </summary>
    public Task<JarAddResult> AddJarsAsync(
        string instanceDir, ModPlatform platform, IReadOnlyList<string> paths, CancellationToken cancellationToken = default) =>
        AddJarsAsync(new LocalModFiles(instanceDir), platform, paths, cancellationToken);

    /// <summary>The same as <see cref="AddJarsAsync(string, ModPlatform, IReadOnlyList{string}, CancellationToken)"/>; the jars are sent to <paramref name="files"/>.</summary>
    public Task<JarAddResult> AddJarsAsync(
        IServerFileSystem files, ModPlatform platform, IReadOnlyList<string> paths, CancellationToken cancellationToken = default) =>
        AddJarsAsync(new ServerModFiles(files), platform, paths, cancellationToken);

    private async Task<JarAddResult> AddJarsAsync(
        IModFiles files, ModPlatform platform, IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        var folder = platform.FolderName;
        var installed = await ListInstalledAsync(files, platform, cancellationToken).ConfigureAwait(false);
        var byHash = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var notJars = new List<string>();
        var skipped = new List<string>();
        var names = installed.Select(m => m.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            var name = Path.GetFileName(path);
            if (!ModrinthClient.IsSafeJarName(name) || !File.Exists(path))
            {
                notJars.Add(name);
            }
            else if (!names.Add(name))
            {
                skipped.Add(name);
            }
            else
            {
                byHash.TryAdd(await Task.Run(() => HashFile(path), cancellationToken).ConfigureAwait(false), path);
            }
        }

        var installedHashes = await files.HashAsync(installed.Select(m => $"{folder}/{m.FileName}").ToList(), cancellationToken).ConfigureAwait(false);

        var identified = await modrinth.IdentifyVersionsAsync(byHash.Keys.Concat(installedHashes).ToList(), cancellationToken).ConfigureAwait(false);
        var knownProjects = installedHashes
            .Select(h => identified.GetValueOrDefault(h)?.ProjectId)
            .OfType<string>()
            .Concat(installed.Select(m => m.ProjectId).OfType<string>())
            .ToHashSet(StringComparer.Ordinal);
        var projects = (await modrinth.GetProjectsAsync(
                byHash.Keys.Select(h => identified.GetValueOrDefault(h)?.ProjectId).OfType<string>().Distinct(StringComparer.Ordinal).ToList(),
                cancellationToken).ConfigureAwait(false))
            .ToDictionary(p => p.Id, StringComparer.Ordinal);

        var present = installed.Select(m => m.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var records = (await ReadRecordsAsync(files, cancellationToken).ConfigureAwait(false)).Where(r => present.Contains(r.FileName)).ToList();
        var added = new List<string>();
        var clientOnly = new List<string>();
        foreach (var (hash, path) in byHash)
        {
            var name = Path.GetFileName(path);
            var version = identified.GetValueOrDefault(hash);
            var project = version is null ? null : projects.GetValueOrDefault(version.ProjectId);
            if (version is not null && !knownProjects.Add(version.ProjectId))
            {
                skipped.Add(project?.Title ?? name);
                continue;
            }

            await files.CopyInAsync(path, JarPath(folder, name), cancellationToken).ConfigureAwait(false);
            if (version is not null)
            {
                records.Add(new InstalledMod(name, version.ProjectId, version.VersionId, project?.Title ?? name));
                await WriteRecordsAsync(files, records, cancellationToken).ConfigureAwait(false);
            }

            added.Add(project?.Title ?? name);
            if (project is { IsClientOnly: true })
            {
                clientOnly.Add(project.Title);
            }
        }

        return new JarAddResult(added, skipped, notJars, clientOnly);
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

    /// <summary>The same as <see cref="Remove"/>, in <paramref name="files"/>.</summary>
    public async Task RemoveAsync(IServerFileSystem files, ModPlatform platform, InstalledMod mod, CancellationToken cancellationToken = default)
    {
        var modFiles = new ServerModFiles(files);
        await modFiles.DeleteAsync(JarPath(platform.FolderName, mod.FileName), cancellationToken).ConfigureAwait(false);
        var records = (await ReadRecordsAsync(modFiles, cancellationToken).ConfigureAwait(false))
            .Where(r => !string.Equals(r.FileName, mod.FileName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        await WriteRecordsAsync(modFiles, records, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The folder next to mods or plugins where turned-off jars wait, e.g. "mods-disabled".</summary>
    public static string DisabledFolderIn(string instanceDir, ModPlatform platform) => Path.Combine(instanceDir, platform.FolderName + "-disabled");

    /// <summary>
    /// Moves the jar to <see cref="DisabledFolderIn"/> instead of deleting it, so it can be moved back by hand.
    /// Only call while the server is stopped.
    /// </summary>
    /// <returns>The path the jar was moved to.</returns>
    public string Disable(string instanceDir, ModPlatform platform, string fileName)
    {
        if (!ModrinthClient.IsSafeJarName(fileName))
        {
            throw new ArgumentException($"\"{fileName}\" is not a jar in the {platform.FolderName} folder.", nameof(fileName));
        }

        var target = DisabledFolderIn(instanceDir, platform);
        Directory.CreateDirectory(target);
        var destination = Path.Combine(target, fileName);
        for (var i = 2; File.Exists(destination); i++)
        {
            destination = Path.Combine(target, $"{Path.GetFileNameWithoutExtension(fileName)}-{i}.jar");
        }

        File.Move(Path.Combine(platform.FolderIn(instanceDir), fileName), destination);
        var records = ReadRecords(instanceDir)
            .Where(r => !string.Equals(r.FileName, fileName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        WriteRecords(instanceDir, records);
        return destination;
    }

    /// <summary>A jar's path in the server folder. Refuses a name that isn't a plain .jar file name.</summary>
    private static string JarPath(string folder, string fileName) =>
        ModrinthClient.IsSafeJarName(fileName)
            ? $"{folder}/{fileName}"
            : throw new ArgumentException($"\"{fileName}\" is not a jar in the {folder} folder.", nameof(fileName));

    internal static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA512.HashData(stream));
    }

    private static List<InstalledMod> ReadRecords(string instanceDir) => ParseRecords(ReadLocalRecords(instanceDir));

    private static string? ReadLocalRecords(string instanceDir)
    {
        var path = Path.Combine(instanceDir, RecordsFileName);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    private static async Task<List<InstalledMod>> ReadRecordsAsync(IModFiles files, CancellationToken cancellationToken) =>
        ParseRecords(await files.ReadTextAsync(RecordsFileName, cancellationToken).ConfigureAwait(false));

    private static List<InstalledMod> ParseRecords(string? json)
    {
        if (json is null)
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<InstalledMod>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            // Only names are lost: the jars still load, and they show by file name until reinstalled.
            return [];
        }
    }

    private static void WriteRecords(string instanceDir, List<InstalledMod> records) =>
        AtomicFile.WriteAllText(Path.Combine(instanceDir, RecordsFileName), JsonSerializer.Serialize(records, JsonOptions));

    private static Task WriteRecordsAsync(IModFiles files, List<InstalledMod> records, CancellationToken cancellationToken) =>
        files.WriteTextAsync(RecordsFileName, JsonSerializer.Serialize(records, JsonOptions), cancellationToken);
}
