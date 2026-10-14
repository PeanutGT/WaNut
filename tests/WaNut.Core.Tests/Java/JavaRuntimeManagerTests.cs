using WaNut.Core.Downloads;
using WaNut.Core.Java;
using WaNut.Core.Tests.TestSupport;

namespace WaNut.Core.Tests.Java;

public sealed class JavaRuntimeManagerTests : IDisposable
{
    private static readonly Uri ZipUrl = new("https://example.com/jre21.zip");

    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private (JavaRuntimeManager Manager, StubHttpHandler Http) Create(string? sha256 = null)
    {
        var zip = SampleData.JreZip(21);
        var http = new StubHttpHandler()
            .Add(AdoptiumClient.LatestJreUrl(21), SampleData.AdoptiumLatest(21, ZipUrl.AbsoluteUri, sha256 ?? Hashes.Sha256(zip), zip.Length))
            .Add(ZipUrl, zip);
        var client = http.CreateClient();
        var paths = new LauncherPaths(_temp.Path);
        paths.EnsureCreated();
        return (new JavaRuntimeManager(paths, new AdoptiumClient(client), new FileDownloader(client)), http);
    }

    [Fact]
    public void FindJavaExe_NotInstalled_ReturnsNull()
    {
        Assert.Null(Create().Manager.FindJavaExe(21));
    }

    [Fact]
    public async Task EnsureInstalledAsync_NotInstalled_DownloadsAndExtracts()
    {
        var (manager, _) = Create();

        var javaExe = await manager.EnsureInstalledAsync(21);

        Assert.True(File.Exists(javaExe));
        Assert.Equal("bin", Path.GetFileName(Path.GetDirectoryName(javaExe)));
        Assert.StartsWith(manager.RuntimeDir(21), javaExe);
        Assert.False(Directory.Exists(manager.RuntimeDir(21) + ".extracting"));
    }

    [Fact]
    public async Task EnsureInstalledAsync_AlreadyInstalled_DoesNotDownloadAgain()
    {
        var (manager, http) = Create();
        await manager.EnsureInstalledAsync(21);
        var requestsAfterFirst = http.Requests.Count;

        await manager.EnsureInstalledAsync(21);

        Assert.Equal(requestsAfterFirst, http.Requests.Count);
    }

    [Fact]
    public async Task EnsureInstalledAsync_BadChecksum_InstallsNothing()
    {
        var (manager, _) = Create(sha256: new string('0', 64));

        await Assert.ThrowsAsync<DownloadVerificationException>(() => manager.EnsureInstalledAsync(21));

        Assert.Null(manager.FindJavaExe(21));
    }

    [Fact]
    public async Task EnsureInstalledAsync_DamagedRuntimeFolder_Reinstalls()
    {
        var (manager, _) = Create();
        Directory.CreateDirectory(Path.Combine(manager.RuntimeDir(21), "leftover"));

        var javaExe = await manager.EnsureInstalledAsync(21);

        Assert.True(File.Exists(javaExe));
        Assert.False(Directory.Exists(Path.Combine(manager.RuntimeDir(21), "leftover")));
    }
}
