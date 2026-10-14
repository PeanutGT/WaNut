using System.Text;
using Microsoft.Extensions.Time.Testing;
using WaNut.Core.Downloads;
using WaNut.Core.Instances;
using WaNut.Core.Java;
using WaNut.Core.Tests.TestSupport;
using WaNut.Core.Versions;

namespace WaNut.Core.Tests.Instances;

public sealed class ServerCreatorTests : IDisposable
{
    private static readonly Uri DetailsUrl = new("https://piston-meta.mojang.com/v1/packages/1.21.1.json");
    private static readonly Uri JarUrl = new("https://piston-data.mojang.com/v1/objects/x/server.jar");
    private static readonly Uri ZipUrl = new("https://example.com/jre21.zip");
    private static readonly byte[] Jar = Encoding.UTF8.GetBytes("server jar bytes");
    private static readonly byte[] PaperJar = Encoding.UTF8.GetBytes("paper jar bytes");
    private static readonly Uri PaperJarUrl = new("https://fill-data.papermc.io/v1/objects/p/paper-1.21.1-133.jar");
    private static readonly byte[] FabricJar = SampleData.FabricServerLauncherJar();

    private readonly TempDir _temp = new();
    private readonly LauncherPaths _paths;
    private readonly StubHttpHandler _http;
    private readonly InstanceStore _store;
    private readonly GameVersion _version;

    public ServerCreatorTests()
    {
        _paths = new LauncherPaths(_temp.Path);
        _paths.EnsureCreated();
        _store = new InstanceStore(_paths);

        var details = Encoding.UTF8.GetBytes(SampleData.VersionDetails(JarUrl.AbsoluteUri, Hashes.Sha1(Jar), Jar.Length, javaMajor: 21));
        var zip = SampleData.JreZip(21);
        _http = new StubHttpHandler()
            .Add(DetailsUrl, details)
            .Add(JarUrl, Jar)
            .Add(AdoptiumClient.LatestJreUrl(21), SampleData.AdoptiumLatest(21, ZipUrl.AbsoluteUri, Hashes.Sha256(zip), zip.Length))
            .Add(ZipUrl, zip)
            .Add(PaperClient.BuildsUrl("1.21.1"), SampleData.PaperBuilds(
                (134, "ALPHA", "https://fill-data.papermc.io/v1/objects/x/paper-1.21.1-134.jar", new string('e', 64)),
                (133, "STABLE", PaperJarUrl.AbsoluteUri, Hashes.Sha256(PaperJar))))
            .Add(PaperJarUrl, PaperJar)
            .Add(FabricClient.LoaderVersionsUrl, SampleData.FabricLoaderVersions)
            .Add(FabricClient.InstallerVersionsUrl, SampleData.FabricInstallerVersions)
            .Add(FabricClient.ServerLauncherUrl("1.1.2"), FabricJar)
            .Add(new Uri(FabricClient.ServerLauncherUrl("1.1.2").AbsoluteUri + ".sha256"), Hashes.Sha256(FabricJar));
        _version = new GameVersion("1.21.1", DateTimeOffset.UnixEpoch, DetailsUrl, Hashes.Sha1(details));
    }

    public void Dispose() => _temp.Dispose();

    private ServerCreator CreateCreator()
    {
        var client = _http.CreateClient();
        var downloader = new FileDownloader(client);
        var java = new JavaRuntimeManager(_paths, new AdoptiumClient(client), downloader);
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero));
        return new ServerCreator(_paths, new MojangVersionService(client, downloader), new PaperClient(client), new FabricClient(client), java, downloader, _store, clock);
    }

    [Fact]
    public async Task CreateAsync_EulaAccepted_CreatesReadyServerFolder()
    {
        var metadata = await CreateCreator().CreateAsync(new CreateServerRequest("My Server", _version, 4096, EulaAccepted: true));

        var dir = _store.InstanceDir(metadata.Id);
        Assert.Equal("my-server", metadata.Id);
        Assert.Equal(ServerType.Vanilla, metadata.Type);
        Assert.Equal("1.21.1", metadata.MinecraftVersion);
        Assert.Equal(21, metadata.JavaMajor);
        Assert.Equal(4096, metadata.MemoryMb);
        Assert.Equal(Jar, await File.ReadAllBytesAsync(Path.Combine(dir, "server.jar")));
        Assert.True(EulaFile.IsAccepted(dir));
        Assert.Equal(metadata, Assert.Single(_store.LoadAll()));
    }

    [Fact]
    public async Task CreateAsync_Paper_UsesNewestStableBuildAsServerJar()
    {
        var metadata = await CreateCreator().CreateAsync(new CreateServerRequest("Paper", _version, 4096, EulaAccepted: true, ServerType.Paper));

        var dir = _store.InstanceDir(metadata.Id);
        Assert.Equal(ServerType.Paper, metadata.Type);
        Assert.Equal("133", metadata.LoaderVersion);
        Assert.Equal("server.jar", metadata.ServerJar);
        Assert.Equal(PaperJar, await File.ReadAllBytesAsync(Path.Combine(dir, "server.jar")));
        Assert.Equal(21, metadata.JavaMajor);
        Assert.DoesNotContain(JarUrl, _http.Requests);
    }

    [Fact]
    public async Task CreateAsync_PaperChecksumWrong_ThrowsAndCreatesNoServer()
    {
        _http.Add(PaperClient.BuildsUrl("1.21.1"), SampleData.PaperBuilds((133, "STABLE", PaperJarUrl.AbsoluteUri, new string('0', 64))));

        await Assert.ThrowsAsync<DownloadVerificationException>(() =>
            CreateCreator().CreateAsync(new CreateServerRequest("Paper", _version, 4096, EulaAccepted: true, ServerType.Paper)));

        Assert.Empty(Directory.GetDirectories(_paths.InstancesDir));
    }

    [Fact]
    public async Task CreateAsync_Fabric_PlacesLauncherNextToVanillaJar()
    {
        var metadata = await CreateCreator().CreateAsync(new CreateServerRequest("Fabric", _version, 4096, EulaAccepted: true, ServerType.Fabric));

        var dir = _store.InstanceDir(metadata.Id);
        Assert.Equal(ServerType.Fabric, metadata.Type);
        Assert.Equal("0.19.5", metadata.LoaderVersion);
        Assert.Equal(FabricClient.LauncherFileName, metadata.ServerJar);
        Assert.Equal(Jar, await File.ReadAllBytesAsync(Path.Combine(dir, "server.jar")));
        Assert.True(Directory.Exists(Path.Combine(dir, "mods")));

        using var launcher = System.IO.Compression.ZipFile.OpenRead(Path.Combine(dir, FabricClient.LauncherFileName));
        using var reader = new StreamReader(launcher.GetEntry("install.properties")!.Open());
        Assert.Equal("fabric-loader-version=0.19.5\ngame-version=1.21.1\n", reader.ReadToEnd());
    }

    [Fact]
    public async Task CreateAsync_FabricChecksumWrong_ThrowsAndCreatesNoServer()
    {
        _http.Add(new Uri(FabricClient.ServerLauncherUrl("1.1.2").AbsoluteUri + ".sha256"), new string('0', 64));

        await Assert.ThrowsAsync<DownloadVerificationException>(() =>
            CreateCreator().CreateAsync(new CreateServerRequest("Fabric", _version, 4096, EulaAccepted: true, ServerType.Fabric)));

        Assert.Empty(Directory.GetDirectories(_paths.InstancesDir));
    }

    [Fact]
    public async Task CreateAsync_FabricTwice_KeepsCachedLauncherUnchanged()
    {
        var creator = CreateCreator();
        await creator.CreateAsync(new CreateServerRequest("One", _version, 2048, EulaAccepted: true, ServerType.Fabric));
        _http.Requests.Clear();

        await creator.CreateAsync(new CreateServerRequest("Two", _version, 2048, EulaAccepted: true, ServerType.Fabric));

        // install.properties goes into each server's copy, so the cached jar still matches Maven's checksum.
        Assert.DoesNotContain(FabricClient.ServerLauncherUrl("1.1.2"), _http.Requests);
        Assert.DoesNotContain(JarUrl, _http.Requests);
    }

    [Fact]
    public async Task CreateAsync_SecondServerSameVersion_ReusesCachedDownloads()
    {
        var creator = CreateCreator();
        await creator.CreateAsync(new CreateServerRequest("One", _version, 2048, EulaAccepted: true));
        _http.Requests.Clear();

        await creator.CreateAsync(new CreateServerRequest("Two", _version, 2048, EulaAccepted: true));

        Assert.DoesNotContain(JarUrl, _http.Requests);
        Assert.DoesNotContain(ZipUrl, _http.Requests);
        Assert.Equal(2, _store.LoadAll().Count);
    }

    [Fact]
    public async Task CreateAsync_EulaNotAccepted_ThrowsAndCreatesNothing()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateCreator().CreateAsync(new CreateServerRequest("My Server", _version, 4096, EulaAccepted: false)));

        Assert.Empty(Directory.GetDirectories(_paths.InstancesDir));
        Assert.Empty(_http.Requests);
    }

    [Fact]
    public async Task CreateAsync_JarChecksumWrong_ThrowsAndCreatesNoServer()
    {
        var details = Encoding.UTF8.GetBytes(SampleData.VersionDetails(JarUrl.AbsoluteUri, new string('0', 40), Jar.Length, javaMajor: 21));
        _http.Add(DetailsUrl, details);
        var version = _version with { DetailsSha1 = Hashes.Sha1(details) };

        await Assert.ThrowsAsync<DownloadVerificationException>(() =>
            CreateCreator().CreateAsync(new CreateServerRequest("My Server", version, 4096, EulaAccepted: true)));

        Assert.Empty(Directory.GetDirectories(_paths.InstancesDir));
    }

    [Fact]
    public async Task CreateAsync_WithProgress_ReportsStagesInOrder()
    {
        var stages = new List<CreateServerStage>();
        var progress = new InlineProgress(p =>
        {
            if (stages.Count == 0 || stages[^1] != p.Stage)
            {
                stages.Add(p.Stage);
            }
        });

        await CreateCreator().CreateAsync(new CreateServerRequest("My Server", _version, 4096, EulaAccepted: true), progress);

        Assert.Equal(
            [CreateServerStage.ReadingVersion, CreateServerStage.DownloadingJava, CreateServerStage.DownloadingServer, CreateServerStage.WritingFiles],
            stages);
    }

    private sealed class InlineProgress(Action<CreateServerProgress> handler) : IProgress<CreateServerProgress>
    {
        public void Report(CreateServerProgress value) => handler(value);
    }
}
