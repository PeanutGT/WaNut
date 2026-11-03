using System.Text;
using WaNut.Core.Downloads;
using WaNut.Core.Instances;
using WaNut.Core.Mods;
using WaNut.Core.Profiling;
using WaNut.Core.Tests.Mods;
using WaNut.Core.Tests.TestSupport;

namespace WaNut.Core.Tests.Profiling;

public sealed class SparkTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly StubHttpHandler _http = new();
    private readonly Spark _spark;

    public SparkTests()
    {
        var client = _http.CreateClient();
        var downloader = new FileDownloader(client);
        _spark = new Spark(new ModLibrary(new ModrinthClient(client), downloader), client, downloader);
    }

    public void Dispose() => _dir.Dispose();

    [Theory]
    [InlineData(ServerType.Vanilla, "1.21.1", SparkSource.None)]
    [InlineData(ServerType.Fabric, "1.16.5", SparkSource.Modrinth)]
    [InlineData(ServerType.Forge, "1.20.1", SparkSource.Modrinth)]
    [InlineData(ServerType.NeoForge, "26.3", SparkSource.Modrinth)]
    [InlineData(ServerType.Paper, "1.20.6", SparkSource.Official)]
    [InlineData(ServerType.Paper, "1.21", SparkSource.BuiltIn)]
    [InlineData(ServerType.Paper, "26.1.2", SparkSource.BuiltIn)]
    public void SourceFor_PicksWhereSparkComesFrom(ServerType type, string version, SparkSource expected) =>
        Assert.Equal(expected, Spark.SourceFor(type, version));

    [Theory]
    [InlineData("spark-1.10.195-fabric.jar", true)]
    [InlineData("SPARK.jar", true)]
    [InlineData("spark_forge.jar", true)]
    [InlineData("sparkly-paintings.jar", false)]
    [InlineData("spark-1.10.195-fabric.jar.disabled", false)]
    public void IsSparkJarName_MatchesOnlySparkJars(string name, bool expected) =>
        Assert.Equal(expected, Spark.IsSparkJarName(name));

    [Fact]
    public void IsInstalled_LooksInTheServersFolder()
    {
        Assert.False(Spark.IsInstalled(_dir.Path, ServerType.Fabric, "1.21.1"));
        Assert.True(Spark.IsInstalled(_dir.Path, ServerType.Paper, "1.21.1"));
        Assert.False(Spark.IsInstalled(_dir.Path, ServerType.Vanilla, "1.21.1"));

        Directory.CreateDirectory(Path.Combine(_dir.Path, "mods"));
        File.WriteAllText(Path.Combine(_dir.Path, "mods", "spark-1.10.195-fabric.jar"), "jar");

        Assert.True(Spark.IsInstalled(_dir.Path, ServerType.Fabric, "1.21.1"));
        Assert.False(Spark.IsInstalled(_dir.Path, ServerType.Paper, "1.20.4"));
    }

    [Theory]
    [InlineData("[12:00:01 INFO]: [⚡] https://spark.lucko.me/AbC123xyz", "https://spark.lucko.me/AbC123xyz")]
    [InlineData("[Server thread/INFO] [spark/]: https://spark.lucko.me/q1w2e3", "https://spark.lucko.me/q1w2e3")]
    [InlineData("[⚡] Profiler is now running!", null)]
    [InlineData("<Steve> look at http://spark.lucko.me/fake", null)]
    public void FindReportUrl_ReadsTheLink(string line, string? expected) =>
        Assert.Equal(expected, Spark.FindReportUrl(line)?.AbsoluteUri);

    [Fact]
    public async Task InstallAsync_OldPaper_DownloadsTheOfficialJarCheckedBySha1()
    {
        var jar = Encoding.UTF8.GetBytes("spark plugin");
        var sha1 = Convert.ToHexStringLower(System.Security.Cryptography.SHA1.HashData(jar));
        var url = new Uri("https://ci.lucko.me/job/spark/547/artifact/spark-1.10.195-bukkit.jar");
        _http.Add(Spark.DownloadIndexUrl, $$$"""{"bukkit":{"fileName":"spark-1.10.195-bukkit.jar","url":"{{{url}}}","sha1":"{{{sha1}}}"}}""")
            .Add(url, jar);

        await _spark.InstallAsync(_dir.Path, ServerType.Paper, "1.20.4");

        Assert.Equal(jar, File.ReadAllBytes(Path.Combine(_dir.Path, "plugins", "spark-1.10.195-bukkit.jar")));
        Assert.True(Spark.IsInstalled(_dir.Path, ServerType.Paper, "1.20.4"));
    }

    [Fact]
    public async Task InstallAsync_OfficialJarWithWrongHash_KeepsNothing()
    {
        var url = new Uri("https://ci.lucko.me/spark-bukkit.jar");
        _http.Add(Spark.DownloadIndexUrl, $$$"""{"bukkit":{"fileName":"spark-bukkit.jar","url":"{{{url}}}","sha1":"{{{new string('0', 40)}}}"}}""")
            .Add(url, "tampered");

        await Assert.ThrowsAsync<DownloadVerificationException>(() => _spark.InstallAsync(_dir.Path, ServerType.Paper, "1.20.4"));

        Assert.False(Spark.IsInstalled(_dir.Path, ServerType.Paper, "1.20.4"));
    }

    [Theory]
    [InlineData("../evil.jar", "https://ci.lucko.me/x.jar")]
    [InlineData("spark-bukkit.jar", "http://ci.lucko.me/x.jar")]
    public async Task InstallAsync_UnsafeDownloadList_IsRefused(string fileName, string url)
    {
        _http.Add(Spark.DownloadIndexUrl, $$$"""{"bukkit":{"fileName":"{{{fileName}}}","url":"{{{url}}}","sha1":"{{{new string('a', 40)}}}"}}""");

        await Assert.ThrowsAsync<InvalidDataException>(() => _spark.InstallAsync(_dir.Path, ServerType.Paper, "1.20.4"));
    }

    [Fact]
    public async Task InstallAsync_Fabric_InstallsTheModrinthProject()
    {
        var platform = ModPlatform.For(ServerType.Fabric)!;
        var jar = Encoding.UTF8.GetBytes("spark mod");
        var sha512 = Convert.ToHexStringLower(System.Security.Cryptography.SHA512.HashData(jar));
        var url = new Uri("https://cdn.modrinth.com/data/l6YH9Als/spark-1.10.195-fabric.jar");
        var version = ModrinthClientTests.Version(
            "v1", "release", "2026-10-01T00:00:00Z", fileName: "spark-1.10.195-fabric.jar", url: url.AbsoluteUri, sha512: sha512)
            .Replace("\"project_id\":\"proj\"", $"\"project_id\":\"{Spark.ModrinthProjectId}\"", StringComparison.Ordinal);
        _http.Add(ModrinthClient.VersionsUrl(Spark.ModrinthProjectId, platform, "1.21.1"), $"[{version}]").Add(url, jar);

        await _spark.InstallAsync(_dir.Path, ServerType.Fabric, "1.21.1");

        Assert.True(Spark.IsInstalled(_dir.Path, ServerType.Fabric, "1.21.1"));
    }

    [Fact]
    public async Task InstallAsync_BuiltIn_Throws() =>
        await Assert.ThrowsAsync<InvalidOperationException>(() => _spark.InstallAsync(_dir.Path, ServerType.Paper, "1.21.4"));
}
