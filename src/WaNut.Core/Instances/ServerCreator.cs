using WaNut.Core.Downloads;
using WaNut.Core.Java;
using WaNut.Core.Players;
using WaNut.Core.Settings;
using WaNut.Core.Versions;

namespace WaNut.Core.Instances;

/// <summary>
/// Creates a ready-to-start Vanilla, Paper or Fabric server: the right Java, the server files and an accepted eula.txt.
/// Every download is checked against a published checksum and cached, so a second server of the same version
/// needs no download.
/// </summary>
public sealed class ServerCreator(
    LauncherPaths paths,
    MojangVersionService versions,
    PaperClient paper,
    FabricClient fabric,
    JavaRuntimeManager java,
    FileDownloader downloader,
    InstanceStore store,
    TimeProvider clock)
{
    private const string VanillaJar = "server.jar";

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
