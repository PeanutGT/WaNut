using System.Collections.Concurrent;
using System.Diagnostics;
using WaNut.Core.Backups;
using WaNut.Core.Downloads;
using WaNut.Core.Hosting;
using WaNut.Core.Http;
using WaNut.Core.Instances;
using WaNut.Core.Java;
using WaNut.Core.Tests.TestSupport;
using WaNut.Core.Versions;

namespace WaNut.Core.Tests.Network;

/// <summary>
/// Talks to the real Mojang and Adoptium services. The end-to-end test downloads about 100 MB.
/// CI skips these; run them with: dotnet test --filter Category=Network
/// </summary>
[Trait("Category", "Network")]
public sealed class LiveApiTests : IDisposable
{
    private readonly HttpClient _http = WaNutHttp.CreateClient("0.0.0-test");

    public void Dispose() => _http.Dispose();

    [Fact]
    public async Task Mojang_Releases_IncludeOldestSupportedAndNewerVersions()
    {
        var releases = await new MojangVersionService(_http, new FileDownloader(_http)).GetReleasesAsync();

        Assert.Equal(MojangVersionService.OldestSupportedVersion, releases[^1].Id);
        Assert.Contains(releases, r => r.Id == "1.21.1");
    }

    [Theory]
    [InlineData("1.16.5", 8)]
    [InlineData("1.21.1", 21)]
    public async Task Mojang_ServerInfo_ReportsJavaVersionAndJar(string id, int expectedJava)
    {
        var service = new MojangVersionService(_http, new FileDownloader(_http));
        var version = (await service.GetReleasesAsync()).Single(r => r.Id == id);

        var info = await service.GetServerInfoAsync(version);

        Assert.Equal(expectedJava, info.JavaMajor);
        Assert.Equal(Uri.UriSchemeHttps, info.JarUrl.Scheme);
        Assert.Equal(40, info.JarSha1.Length);
    }

    [Theory]
    [InlineData("1.16.5")]
    [InlineData("1.20.1")]
    [InlineData("1.21.1")]
    public async Task Forge_Installer_MatchesMavenSha1(string gameVersion)
    {
        var installer = await new ForgeClient(_http).GetInstallerAsync(gameVersion);

        Assert.Equal(Hashes.Sha1(await _http.GetByteArrayAsync(installer.JarUrl)), installer.Sha1);
    }

    [Fact]
    public async Task NeoForge_Installer_MatchesMavenSha1()
    {
        var installer = await new NeoForgeClient(_http).GetInstallerAsync("1.21.1");

        Assert.Equal(Hashes.Sha1(await _http.GetByteArrayAsync(installer.JarUrl)), installer.Sha1);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(17)]
    [InlineData(21)]
    public async Task Adoptium_LatestJre_ReturnsHttpsZipWithSha256(int major)
    {
        var package = await new AdoptiumClient(_http).GetLatestJreAsync(major);

        Assert.Equal(Uri.UriSchemeHttps, package.Link.Scheme);
        Assert.EndsWith(".zip", package.Link.AbsolutePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(64, package.Sha256.Length);
    }

    [Fact]
    public async Task ServerCreator_RealDownloads_CreatesServerWithWorkingJava()
    {
        using var temp = new TempDir();
        var paths = new LauncherPaths(temp.Path);
        paths.EnsureCreated();
        var downloader = new FileDownloader(_http);
        var versions = new MojangVersionService(_http, downloader);
        var java = new JavaRuntimeManager(paths, new AdoptiumClient(_http), downloader);
        var store = new InstanceStore(paths);
        var creator = new ServerCreator(paths, versions, new PaperClient(_http), new FabricClient(_http), new ForgeClient(_http), new NeoForgeClient(_http), new ForgeInstallerRunner(), java, downloader, store, new BackupStore(TimeProvider.System), TimeProvider.System);
        var version = (await versions.GetReleasesAsync()).Single(r => r.Id == "1.21.1");

        var metadata = await creator.CreateAsync(new CreateServerRequest("Live Test", version, 2048, EulaAccepted: true));

        var dir = store.InstanceDir(metadata.Id);
        Assert.True(new FileInfo(Path.Combine(dir, "server.jar")).Length > 1_000_000);
        Assert.True(EulaFile.IsAccepted(dir));
        var javaExe = java.FindJavaExe(metadata.JavaMajor);
        Assert.NotNull(javaExe);

        if (OperatingSystem.IsWindows())
        {
            using var process = Process.Start(new ProcessStartInfo(javaExe, "-version") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true })!;
            var output = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.Equal(0, process.ExitCode);
            Assert.Contains("21", output);
        }
    }

    [Theory]
    [InlineData(ServerType.Vanilla)]
    [InlineData(ServerType.Paper)]
    [InlineData(ServerType.Fabric)]
    public async Task ServerProcess_RealServer_StartsOnlineAndStopsCleanly(ServerType type)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = new TempDir();
        var paths = new LauncherPaths(temp.Path);
        paths.EnsureCreated();
        var downloader = new FileDownloader(_http);
        var versions = new MojangVersionService(_http, downloader);
        var java = new JavaRuntimeManager(paths, new AdoptiumClient(_http), downloader);
        var store = new InstanceStore(paths);
        var creator = new ServerCreator(paths, versions, new PaperClient(_http), new FabricClient(_http), new ForgeClient(_http), new NeoForgeClient(_http), new ForgeInstallerRunner(), java, downloader, store, new BackupStore(TimeProvider.System), TimeProvider.System);
        var version = (await versions.GetReleasesAsync()).Single(r => r.Id == "1.21.1");
        var metadata = await creator.CreateAsync(new CreateServerRequest("Live Run", version, 2048, EulaAccepted: true, type));
        var launch = await new ServerLaunchPlanner(store, java).PrepareAsync(metadata);
        var lines = new ConcurrentQueue<string>();

        using var server = new ServerHost().Start(metadata.Id, launch);
        server.OutputReceived += lines.Enqueue;
        await FakeServer.WaitUntilAsync(() => server.Status == ServerStatus.Online || !server.IsRunning, timeoutMs: 300_000);
        Assert.Equal(ServerStatus.Online, server.Status);
        Assert.True(server.IsTrackedByJob);

        await server.SendCommandAsync("say hello from WaNut");
        await FakeServer.WaitUntilAsync(() => lines.Any(l => l.Contains("hello from WaNut", StringComparison.Ordinal)));
        var stopped = await server.StopAsync(TimeSpan.FromSeconds(60));

        Assert.True(stopped);
        Assert.Equal(0, await server.Exited);
        Assert.True(Directory.Exists(Path.Combine(store.InstanceDir(metadata.Id), "world")));
    }

    [Fact]
    public async Task Paper_ReleaseVersions_HaveStableBuildWithSha256()
    {
        var paper = new PaperClient(_http);
        var ids = await paper.GetVersionIdsAsync();

        Assert.Contains("1.16.5", ids);
        Assert.DoesNotContain(ids, id => id.Contains('-', StringComparison.Ordinal));
        var build = await paper.GetBestBuildAsync("1.21.4");
        Assert.Equal("STABLE", build.Channel);
        Assert.Equal(64, build.Sha256.Length);
    }

    [Fact]
    public async Task Fabric_LatestLauncher_MatchesMavenChecksum()
    {
        var fabric = new FabricClient(_http);
        Assert.Contains("1.21.4", await fabric.GetStableGameVersionsAsync());

        var launcher = await fabric.GetLatestLauncherAsync();
        var bytes = await new FileDownloader(_http).DownloadBytesAsync(launcher.JarUrl, ExpectedHash.Sha256(launcher.Sha256));

        Assert.True(bytes.Length > 50_000);
    }
}
