using WaNut.Core.Backups;
using WaNut.Core.Downloads;
using WaNut.Core.Java;
using WaNut.Core.Players;
using WaNut.Core.Settings;
using WaNut.Core.Versions;

namespace WaNut.Core.Instances;

/// <summary>
/// Creates a ready-to-start Vanilla, Paper or Fabric server: the right Java, the server files and an accepted eula.txt.
/// Also changes an existing server's type or Minecraft version. Every download is checked against a published
/// checksum and cached, so a second server of the same version needs no download.
/// </summary>
public sealed class ServerCreator(
    LauncherPaths paths,
    MojangVersionService versions,
    PaperClient paper,
    FabricClient fabric,
    JavaRuntimeManager java,
    FileDownloader downloader,
    InstanceStore store,
    BackupStore backups,
    TimeProvider clock)
{
    private const string VanillaJar = "server.jar";

    // Folders the server jars unpack on their first start (Mojang's bundler, Paper and Fabric). They belong to one
    // version, so they go when the version changes and come back on the next start.
    private static readonly string[] UnpackedFolders = ["libraries", "versions", ".fabric"];

    // Paper's own copy of the Vanilla jar it patches, in a "cache" folder inside the server folder.
    private const string PaperCacheFolder = "cache";

    /// <summary>
    /// True when <paramref name="target"/> is an older Minecraft version than the server's. Opening a world in an
    /// older version than the one that last saved it can break it.
    /// </summary>
    public static bool IsDowngrade(InstanceMetadata server, string target) =>
        GameFeatures.ParseVersion(target) < GameFeatures.ParseVersion(server.MinecraftVersion);

    /// <summary>
    /// Switches a stopped server to another type (Vanilla, Paper, Fabric) or Minecraft version. New files are
    /// downloaded and checked first, then the world is backed up (as a "change" backup), and only then are the
    /// server files swapped. The world, mods, plugins and settings stay; game settings are carried over to the
    /// keys or game rules the new version uses. Returns the updated metadata, already saved.
    /// </summary>
    public async Task<InstanceMetadata> ChangeAsync(
        InstanceMetadata server,
        ServerType type,
        GameVersion version,
        IProgress<CreateServerProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (server.Type == type && server.MinecraftVersion == version.Id)
        {
            throw new ArgumentException("The server already has this type and version.", nameof(version));
        }

        progress?.Report(new CreateServerProgress(CreateServerStage.ReadingVersion));
        var vanilla = await versions.GetServerInfoAsync(version, cancellationToken).ConfigureAwait(false);
        await java.EnsureInstalledAsync(vanilla.JavaMajor, StageProgress(progress, CreateServerStage.DownloadingJava), cancellationToken).ConfigureAwait(false);
        var files = await DownloadServerFilesAsync(type, vanilla, StageProgress(progress, CreateServerStage.DownloadingServer), cancellationToken).ConfigureAwait(false);

        var dir = store.InstanceDir(server.Id);
        progress?.Report(new CreateServerProgress(CreateServerStage.BackingUp));
        await backups.CreateAsync(dir, BackupReason.Change, cancellationToken: cancellationToken).ConfigureAwait(false);

        progress?.Report(new CreateServerProgress(CreateServerStage.WritingFiles));
        var settings = GameSettingsStore.Load(dir, server);
        ReplaceServerFiles(dir, server, type, files, vanilla.VersionId);

        var changed = server with
        {
            Type = type,
            MinecraftVersion = vanilla.VersionId,
            JavaMajor = vanilla.JavaMajor,
            LoaderVersion = files.LoaderVersion,
            ServerJar = files.LaunchJar,
        };

        // A setting can move between server.properties and the game rules from one version to another (pvp did in
        // 1.21.9), so re-save the old values against the new version's layout.
        changed = GameSettingsStore.Save(dir, changed, GameSettingsStore.Load(dir, changed), settings);
        store.Save(changed);
        return changed;
    }

    public async Task<InstanceMetadata> CreateAsync(
        CreateServerRequest request,
        IProgress<CreateServerProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!request.EulaAccepted)
        {
            throw new InvalidOperationException("The Minecraft EULA must be accepted before a server can be created.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        var owner = string.IsNullOrWhiteSpace(request.OwnerName) ? null : request.OwnerName.Trim();
        if (owner is not null && !PlayerLists.IsValidName(owner))
        {
            // The name becomes part of a console command, so only real player names get through.
            throw new ArgumentException("Not a valid Minecraft player name.", nameof(request));
        }

        // Mojang's data gives the Java version for every server type (blueprint 1.A).
        progress?.Report(new CreateServerProgress(CreateServerStage.ReadingVersion));
        var vanilla = await versions.GetServerInfoAsync(request.Version, cancellationToken).ConfigureAwait(false);

        await java.EnsureInstalledAsync(vanilla.JavaMajor, StageProgress(progress, CreateServerStage.DownloadingJava), cancellationToken).ConfigureAwait(false);

        var files = await DownloadServerFilesAsync(request.Type, vanilla, StageProgress(progress, CreateServerStage.DownloadingServer), cancellationToken).ConfigureAwait(false);

        progress?.Report(new CreateServerProgress(CreateServerStage.WritingFiles));
        var id = store.CreateFolder(request.Name);
        var dir = store.InstanceDir(id);
        try
        {
            var now = clock.GetLocalNow();
            var metadata = new InstanceMetadata
            {
                Id = id,
                Name = request.Name.Trim(),
                Type = request.Type,
                MinecraftVersion = vanilla.VersionId,
                JavaMajor = vanilla.JavaMajor,
                MemoryMb = request.MemoryMb,
                LoaderVersion = files.LoaderVersion,
                ServerJar = files.LaunchJar,
                CreatedAt = now,
                PendingCommands = owner is null
                    ? []
                    : [PlayerLists.AddCommand(PlayerList.Whitelist, owner), PlayerLists.AddCommand(PlayerList.Operators, owner)],
            };

            foreach (var (cached, fileName) in files.Copies)
            {
                File.Copy(cached, Path.Combine(dir, fileName));
            }

            if (request.Type == ServerType.Fabric)
            {
                FabricClient.WriteInstallProperties(Path.Combine(dir, FabricClient.LauncherFileName), files.LoaderVersion!, vanilla.VersionId);
                Directory.CreateDirectory(Path.Combine(dir, "mods"));
            }

            EulaFile.WriteAccepted(dir, now);
            GameSettingsStore.WriteDefaults(dir, metadata.Name);
            store.Save(metadata);
            return metadata;
        }
        catch
        {
            // The folder was created a moment ago by this call and holds no world yet, so removing it is safe.
            Directory.Delete(dir, recursive: true);
            throw;
        }
    }

    /// <summary>
    /// Copies the new files in under temporary names first, so a full disk fails before any old file is gone.
    /// </summary>
    private static void ReplaceServerFiles(string dir, InstanceMetadata old, ServerType type, ServerFiles files, string gameVersion)
    {
        var staged = new List<(string Temp, string Final)>();
        try
        {
            foreach (var (cached, fileName) in files.Copies)
            {
                var temp = Path.Combine(dir, fileName + ".wanut-new");
                File.Copy(cached, temp, overwrite: true);
                staged.Add((temp, Path.Combine(dir, fileName)));
            }

            if (type == ServerType.Fabric)
            {
                var launcher = staged.Single(s => Path.GetFileName(s.Final) == FabricClient.LauncherFileName).Temp;
                FabricClient.WriteInstallProperties(launcher, files.LoaderVersion!, gameVersion);
            }
        }
        catch
        {
            foreach (var (temp, _) in staged)
            {
                File.Delete(temp);
            }

            throw;
        }

        // instance.json can be edited by hand, so only a plain file name from it is ever deleted.
        var oldJars = new[] { VanillaJar, FabricClient.LauncherFileName, old.ServerJar }
            .Where(name => name.Length > 0 && Path.GetFileName(name) == name && name is not "." and not "..")
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var name in oldJars)
        {
            File.Delete(Path.Combine(dir, name));
        }

        var unpacked = old.Type == ServerType.Paper ? UnpackedFolders.Append(PaperCacheFolder) : UnpackedFolders;
        foreach (var folder in unpacked)
        {
            var path = Path.Combine(dir, folder);
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }

        foreach (var (temp, final) in staged)
        {
            File.Move(temp, final, overwrite: true);
        }

        if (type == ServerType.Fabric)
        {
            Directory.CreateDirectory(Path.Combine(dir, "mods"));
        }
    }

    private async Task<ServerFiles> DownloadServerFilesAsync(
        ServerType type,
        VanillaServerInfo vanilla,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        switch (type)
        {
            case ServerType.Vanilla:
                return new ServerFiles(VanillaJar, null, [(await CacheVanillaAsync(vanilla, progress, cancellationToken).ConfigureAwait(false), VanillaJar)]);

            case ServerType.Paper:
                {
                    // Paper's jar fetches and verifies the Vanilla files it patches on its first start.
                    var build = await paper.GetBestBuildAsync(vanilla.VersionId, cancellationToken).ConfigureAwait(false);
                    var cached = Path.Combine(paths.CacheDir, "paper", vanilla.VersionId, $"paper-{vanilla.VersionId}-{build.Id}.jar");
                    await CacheAsync(build.JarUrl, cached, ExpectedHash.Sha256(build.Sha256), progress, cancellationToken).ConfigureAwait(false);
                    return new ServerFiles(VanillaJar, build.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), [(cached, VanillaJar)]);
                }

            case ServerType.Fabric:
                {
                    // Fabric's launcher loads the Vanilla server.jar placed next to it, so it never downloads one itself.
                    var launcher = await fabric.GetLatestLauncherAsync(cancellationToken).ConfigureAwait(false);
                    var vanillaJar = await CacheVanillaAsync(vanilla, progress, cancellationToken).ConfigureAwait(false);
                    var cached = Path.Combine(paths.CacheDir, "fabric", $"fabric-installer-{launcher.InstallerVersion}-server.jar");
                    await CacheAsync(launcher.JarUrl, cached, ExpectedHash.Sha256(launcher.Sha256), progress, cancellationToken).ConfigureAwait(false);
                    return new ServerFiles(FabricClient.LauncherFileName, launcher.LoaderVersion, [(vanillaJar, VanillaJar), (cached, FabricClient.LauncherFileName)]);
                }

            default:
                throw new NotSupportedException($"Creating {type} servers is not supported yet.");
        }
    }

    private async Task<string> CacheVanillaAsync(VanillaServerInfo vanilla, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        var cached = Path.Combine(paths.CacheDir, "vanilla", vanilla.VersionId, VanillaJar);
        await CacheAsync(vanilla.JarUrl, cached, ExpectedHash.Sha1(vanilla.JarSha1), progress, cancellationToken).ConfigureAwait(false);
        return cached;
    }

    private async Task CacheAsync(Uri url, string cached, ExpectedHash hash, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        if (!File.Exists(cached) || !hash.MatchesFile(cached))
        {
            await downloader.DownloadAsync(url, cached, hash, progress, cancellationToken).ConfigureAwait(false);
        }
    }

    private static IProgress<DownloadProgress>? StageProgress(IProgress<CreateServerProgress>? progress, CreateServerStage stage)
    {
        if (progress is null)
        {
            return null;
        }

        progress.Report(new CreateServerProgress(stage));
        // Forward synchronously so download updates never arrive after the next stage has started.
        return new SyncProgress<DownloadProgress>(p => progress.Report(new CreateServerProgress(stage, p.Fraction)));
    }

    /// <param name="LaunchJar">The jar java.exe runs.</param>
    /// <param name="Copies">Cached files and the names they get in the server folder.</param>
    private sealed record ServerFiles(string LaunchJar, string? LoaderVersion, IReadOnlyList<(string Cached, string FileName)> Copies);
}
