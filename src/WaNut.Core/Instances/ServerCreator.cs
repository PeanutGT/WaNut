using WaNut.Core.Downloads;
using WaNut.Core.Java;
using WaNut.Core.Versions;

namespace WaNut.Core.Instances;

/// <summary>
/// Creates a ready-to-start Vanilla server: the right Java, the official server.jar and an accepted eula.txt.
/// </summary>
public sealed class ServerCreator(
    LauncherPaths paths,
    MojangVersionService versions,
    JavaRuntimeManager java,
    FileDownloader downloader,
    InstanceStore store,
    TimeProvider clock)
{
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

        progress?.Report(new CreateServerProgress(CreateServerStage.ReadingVersion));
        var server = await versions.GetServerInfoAsync(request.Version, cancellationToken).ConfigureAwait(false);

        await java.EnsureInstalledAsync(server.JavaMajor, StageProgress(progress, CreateServerStage.DownloadingJava), cancellationToken).ConfigureAwait(false);

        var cachedJar = Path.Combine(paths.CacheDir, "vanilla", server.VersionId, "server.jar");
        var hash = ExpectedHash.Sha1(server.JarSha1);
        if (!File.Exists(cachedJar) || !hash.MatchesFile(cachedJar))
        {
            await downloader.DownloadAsync(server.JarUrl, cachedJar, hash, StageProgress(progress, CreateServerStage.DownloadingServer), cancellationToken).ConfigureAwait(false);
        }

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
                Type = ServerType.Vanilla,
                MinecraftVersion = server.VersionId,
                JavaMajor = server.JavaMajor,
                MemoryMb = request.MemoryMb,
                CreatedAt = now,
            };

            File.Copy(cachedJar, Path.Combine(dir, metadata.ServerJar));
            EulaFile.WriteAccepted(dir, now);
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
}
