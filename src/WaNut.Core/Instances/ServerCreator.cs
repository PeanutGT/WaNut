using WaNut.Core.Backups;
using WaNut.Core.Downloads;
using WaNut.Core.Java;
using WaNut.Core.Players;
using WaNut.Core.Settings;
using WaNut.Core.Versions;
using WaNut.Core.Worlds;

namespace WaNut.Core.Instances;

/// <summary>
/// Creates a ready-to-start Vanilla, Paper, Fabric, Forge or NeoForge server: the right Java, the server files and an accepted eula.txt.
/// Also changes an existing server's type or Minecraft version. Every download is checked against a published
/// checksum and cached, so a second server of the same version needs no download.
/// </summary>
public sealed class ServerCreator(
    LauncherPaths paths,
    MojangVersionService versions,
    PaperClient paper,
    FabricClient fabric,
    ForgeClient forge,
    NeoForgeClient neoForge,
    ForgeInstallerRunner forgeInstaller,
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

    // What the Forge and NeoForge installers put next to libraries/: the launch jar (Forge up to 1.16.5), Minecraft's
    // own jar and the start scripts. They belong to one version, like the unpacked folders.
    private static readonly string[] ForgeFilePatterns = ["forge-*.jar", "neoforge-*.jar", "minecraft_server.*.jar", "run.bat", "run.sh", "user_jvm_args.txt"];

    // Written into a cached installer output once the installer finished, so a half-finished install is never reused.
    private const string InstalledMarker = ".wanut-installed";

    // Where a changed server's new Forge or NeoForge files wait while the old ones are removed.
    private const string StagingFolder = ".wanut-new";

    private const string ModsFolder = "mods";

    /// <summary>True for the types that load mods from the mods folder.</summary>
    public static bool LoadsMods(ServerType type) => type is ServerType.Fabric or ServerType.Forge or ServerType.NeoForge;

    /// <summary>
    /// True when a change from <paramref name="from"/> to <paramref name="to"/> moves the mods folder aside, because
    /// one mod loader can't load another's mods. Going to Vanilla or Paper leaves it, since those ignore it.
    /// </summary>
    public static bool MovesMods(ServerType from, ServerType to) => from != to && LoadsMods(from) && LoadsMods(to);

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
        var javaExe = await java.EnsureInstalledAsync(vanilla.JavaMajor, StageProgress(progress, CreateServerStage.DownloadingJava), cancellationToken).ConfigureAwait(false);
        var files = await DownloadServerFilesAsync(type, vanilla, javaExe, null, progress, cancellationToken).ConfigureAwait(false);

        var dir = store.InstanceDir(server.Id);
        progress?.Report(new CreateServerProgress(CreateServerStage.BackingUp));
        await backups.CreateAsync(dir, BackupReason.Change, cancellationToken: cancellationToken).ConfigureAwait(false);

        progress?.Report(new CreateServerProgress(CreateServerStage.WritingFiles));
        var settings = GameSettingsStore.Load(dir, server);
        ReplaceServerFiles(dir, server, type, files, vanilla.VersionId);
        MoveDimensionFolders(dir, server, type, vanilla.VersionId);
        if (MovesMods(server.Type, type))
        {
            MoveModsAside(dir, server.Type);
        }

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

        var javaExe = await java.EnsureInstalledAsync(vanilla.JavaMajor, StageProgress(progress, CreateServerStage.DownloadingJava), cancellationToken).ConfigureAwait(false);

        var pinnedLoader = request.Type is ServerType.Forge or ServerType.NeoForge ? request.LoaderVersion : null;
        var files = await DownloadServerFilesAsync(request.Type, vanilla, javaExe, pinnedLoader, progress, cancellationToken).ConfigureAwait(false);

        var loaderVersion = request.Type == ServerType.Fabric && request.LoaderVersion is { } pinned ? pinned : files.LoaderVersion;

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
                LoaderVersion = loaderVersion,
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

            if (files.Tree is not null)
            {
                CopyTree(files.Tree, dir);
            }

            if (request.Type == ServerType.Fabric)
            {
                FabricClient.WriteInstallProperties(Path.Combine(dir, FabricClient.LauncherFileName), loaderVersion!, vanilla.VersionId);
            }

            if (LoadsMods(request.Type))
            {
                Directory.CreateDirectory(Path.Combine(dir, ModsFolder));
            }

            EulaFile.WriteAccepted(dir, now);
            GameSettingsStore.WriteDefaults(dir, metadata.Name, vanilla.VersionId, request.World);
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
        var stagedTree = Path.Combine(dir, StagingFolder);
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

            if (files.Tree is not null)
            {
                // libraries/ is replaced as a whole below, so the new one waits in a folder of its own.
                if (Directory.Exists(stagedTree))
                {
                    Directory.Delete(stagedTree, recursive: true);
                }

                CopyTree(files.Tree, stagedTree);
            }
        }
        catch
        {
            foreach (var (temp, _) in staged)
            {
                File.Delete(temp);
            }

            if (Directory.Exists(stagedTree))
            {
                Directory.Delete(stagedTree, recursive: true);
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

        if (old.Type is ServerType.Forge or ServerType.NeoForge)
        {
            foreach (var file in ForgeFilePatterns.SelectMany(pattern => Directory.EnumerateFiles(dir, pattern)).ToList())
            {
                File.Delete(file);
            }
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

        if (files.Tree is not null)
        {
            MoveTreeInto(stagedTree, dir);
        }

        if (LoadsMods(type))
        {
            Directory.CreateDirectory(Path.Combine(dir, ModsFolder));
        }
    }

    /// <summary>
    /// Renames mods/ to mods-&lt;old type&gt; (or mods-&lt;old type&gt;-2 and so on), so nothing is lost, and leaves an
    /// empty mods/ for the new loader.
    /// </summary>
    private static void MoveModsAside(string dir, ServerType oldType)
    {
        var mods = Path.Combine(dir, ModsFolder);
        if (!Directory.Exists(mods) || !Directory.EnumerateFileSystemEntries(mods).Any())
        {
            return;
        }

        var baseName = $"{ModsFolder}-{oldType.ToString().ToLowerInvariant()}";
        var target = Path.Combine(dir, baseName);
        for (var n = 2; Directory.Exists(target) || File.Exists(target); n++)
        {
            target = Path.Combine(dir, $"{baseName}-{n}");
        }

        Directory.Move(mods, target);
        Directory.CreateDirectory(mods);
    }

    private static void CopyTree(string source, string target)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            if (relative == InstalledMarker)
            {
                continue;
            }

            var destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }

    /// <summary>Moves everything in <paramref name="source"/> into <paramref name="target"/>, then removes it.</summary>
    private static void MoveTreeInto(string source, string target)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).ToList())
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Move(file, destination, overwrite: true);
        }

        Directory.Delete(source, recursive: true);
    }

    /// <summary>
    /// Moving between Paper's split layout (up to 1.21.x) and the Vanilla one moves the Nether and End folders too,
    /// or the new server would start with a fresh Nether and End. From 26.x every type keeps all dimensions inside
    /// the world, and Paper upgrades its own old layout. Runs after the change backup, which holds both layouts' folders.
    /// </summary>
    private static void MoveDimensionFolders(string dir, InstanceMetadata old, ServerType type, string gameVersion)
    {
        var fromSplit = WorldLayout.SplitsDimensions(old.Type, old.MinecraftVersion);
        var toSplit = WorldLayout.SplitsDimensions(type, gameVersion);
        if (fromSplit == toSplit || (!fromSplit && !WorldLayout.IsLegacyVersion(old.MinecraftVersion)))
        {
            return;
        }

        var level = GameSettingsStore.LevelName(dir);
        if (toSplit)
        {
            WorldLayout.Split(dir, level);
        }
        else
        {
            WorldLayout.Join(dir, level);
        }
    }

    private async Task<ServerFiles> DownloadServerFilesAsync(
        ServerType type,
        VanillaServerInfo vanilla,
        string javaExe,
        string? pinnedLoader,
        IProgress<CreateServerProgress>? stages,
        CancellationToken cancellationToken)
    {
        var progress = StageProgress(stages, CreateServerStage.DownloadingServer);
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

            case ServerType.Forge:
            case ServerType.NeoForge:
                {
                    var installer = type == ServerType.Forge
                        ? await forge.GetInstallerAsync(vanilla.VersionId, pinnedLoader, cancellationToken).ConfigureAwait(false)
                        : await neoForge.GetInstallerAsync(vanilla.VersionId, pinnedLoader, cancellationToken).ConfigureAwait(false);
                    var tree = await CacheForgeInstallAsync(installer, javaExe, progress, stages, cancellationToken).ConfigureAwait(false);
                    return new ServerFiles(ForgeLaunchTarget(tree, installer), installer.LoaderVersion, [], tree);
                }

            default:
                throw new NotSupportedException($"Creating {type} servers is not supported yet.");
        }
    }

    /// <summary>
    /// Runs the installer once per loader version into cache/&lt;loader&gt;/&lt;version&gt;/server, so a second server of
    /// the same version is a plain copy. Returns that folder.
    /// </summary>
    private async Task<string> CacheForgeInstallAsync(
        ForgeInstaller installer,
        string javaExe,
        IProgress<DownloadProgress>? progress,
        IProgress<CreateServerProgress>? stages,
        CancellationToken cancellationToken)
    {
        var root = Path.Combine(paths.CacheDir, installer.Loader.ToString().ToLowerInvariant(), installer.FullVersion);
        var tree = Path.Combine(root, "server");
        if (File.Exists(Path.Combine(tree, InstalledMarker)))
        {
            return tree;
        }

        var jar = Path.Combine(root, Path.GetFileName(installer.JarUrl.LocalPath));
        await CacheAsync(installer.JarUrl, jar, ExpectedHash.Sha1(installer.Sha1), progress, cancellationToken).ConfigureAwait(false);

        stages?.Report(new CreateServerProgress(CreateServerStage.InstallingLoader));
        if (Directory.Exists(tree))
        {
            Directory.Delete(tree, recursive: true);
        }

        Directory.CreateDirectory(tree);
        await forgeInstaller.RunAsync(javaExe, jar, tree, cancellationToken).ConfigureAwait(false);

        // The installer's log is of no use in the server folder.
        foreach (var file in Directory.EnumerateFiles(tree, "*.log").ToList())
        {
            File.Delete(file);
        }

        ForgeLaunchTarget(tree, installer);
        await File.WriteAllTextAsync(Path.Combine(tree, InstalledMarker), installer.FullVersion, cancellationToken).ConfigureAwait(false);
        return tree;
    }

    /// <summary>
    /// What java.exe starts. From Minecraft 1.17 the installers write an argument file
    /// (libraries/.../win_args.txt) that names the classpath and main class, passed as "@file". Forge for 1.16.5
    /// writes a jar to run with -jar instead.
    /// </summary>
    public static string ForgeLaunchTarget(string installDir, ForgeInstaller installer)
    {
        var group = installer.Loader == ServerLoader.Forge ? "net/minecraftforge/forge" : "net/neoforged/neoforge";
        var argsFile = $"libraries/{group}/{installer.FullVersion}/win_args.txt";
        if (File.Exists(Path.Combine(installDir, argsFile)))
        {
            return "@" + argsFile;
        }

        var prefix = installer.Loader == ServerLoader.Forge ? "forge" : "neoforge";
        foreach (var suffix in new[] { string.Empty, "-universal", "-shim" })
        {
            var jar = $"{prefix}-{installer.FullVersion}{suffix}.jar";
            if (File.Exists(Path.Combine(installDir, jar)))
            {
                return jar;
            }
        }

        throw new ForgeInstallException(0, "The installer finished but left no server to start.");
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

    /// <param name="LaunchJar">The jar java.exe runs, or "@" and an argument file.</param>
    /// <param name="Copies">Cached files and the names they get in the server folder.</param>
    /// <param name="Tree">A cached folder copied into the server folder as a whole (a Forge or NeoForge install).</param>
    private sealed record ServerFiles(string LaunchJar, string? LoaderVersion, IReadOnlyList<(string Cached, string FileName)> Copies, string? Tree = null);
}
