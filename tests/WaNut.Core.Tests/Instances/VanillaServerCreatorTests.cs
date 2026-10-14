using System.Text;
using Microsoft.Extensions.Time.Testing;
using WaNut.Core.Downloads;
using WaNut.Core.Instances;
using WaNut.Core.Java;
using WaNut.Core.Tests.TestSupport;
using WaNut.Core.Versions;

namespace WaNut.Core.Tests.Instances;

public sealed class VanillaServerCreatorTests : IDisposable
{
    private static readonly Uri DetailsUrl = new("https://piston-meta.mojang.com/v1/packages/1.21.1.json");
    private static readonly Uri JarUrl = new("https://piston-data.mojang.com/v1/objects/x/server.jar");
    private static readonly Uri ZipUrl = new("https://example.com/jre21.zip");
    private static readonly byte[] Jar = Encoding.UTF8.GetBytes("server jar bytes");

    private readonly TempDir _temp = new();
    private readonly LauncherPaths _paths;
    private readonly StubHttpHandler _http;
    private readonly InstanceStore _store;
    private readonly GameVersion _version;

    public VanillaServerCreatorTests()
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
            .Add(ZipUrl, zip);
        _version = new GameVersion("1.21.1", DateTimeOffset.UnixEpoch, DetailsUrl, Hashes.Sha1(details));
    }

    public void Dispose() => _temp.Dispose();

    private VanillaServerCreator CreateCreator()
    {
        var client = _http.CreateClient();
        var downloader = new FileDownloader(client);
        var java = new JavaRuntimeManager(_paths, new AdoptiumClient(client), downloader);
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero));
        return new VanillaServerCreator(_paths, new MojangVersionService(client, downloader), java, downloader, _store, clock);
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
