using System.Text;
using WaNut.Core.Tests.TestSupport;
using WaNut.Core.Versions;

namespace WaNut.Core.Tests.Versions;

public sealed class MojangVersionServiceTests
{
    private static MemoryStream Json(string json) => new(Encoding.UTF8.GetBytes(json));

    [Fact]
    public void ParseReleases_MixedTypes_ReturnsReleasesNewestFirstDownTo1165()
    {
        var manifest = SampleData.Manifest(
            ("1.21.1", "release", "2024-08-08T12:24:45+00:00", "https://piston-meta.mojang.com/v1/a/1.21.1.json", "a1"),
            ("24w33a", "snapshot", "2024-08-15T12:00:00+00:00", "https://piston-meta.mojang.com/v1/b/24w33a.json", "b2"),
            ("1.16.5", "release", "2021-01-14T16:05:32+00:00", "https://piston-meta.mojang.com/v1/c/1.16.5.json", "c3"),
            ("1.17", "release", "2021-06-08T11:00:40+00:00", "https://piston-meta.mojang.com/v1/d/1.17.json", "d4"),
            ("1.16.4", "release", "2020-10-29T15:49:37+00:00", "https://piston-meta.mojang.com/v1/e/1.16.4.json", "e5"),
            ("b1.7.3", "old_beta", "2011-07-07T22:00:00+00:00", "https://piston-meta.mojang.com/v1/f/b1.7.3.json", "f6"));

        var releases = MojangVersionService.ParseReleases(Json(manifest));

        Assert.Equal(["1.21.1", "1.17", "1.16.5"], releases.Select(r => r.Id));
        Assert.Equal("a1", releases[0].DetailsSha1);
        Assert.Equal(new Uri("https://piston-meta.mojang.com/v1/a/1.21.1.json"), releases[0].DetailsUrl);
    }

    [Fact]
    public void ParseServerInfo_WithJavaVersion_UsesMojangJavaVersion()
    {
        var details = SampleData.VersionDetails("https://piston-data.mojang.com/v1/objects/abc/server.jar", "abc", 51_000_000, javaMajor: 21);

        var info = MojangVersionService.ParseServerInfo("1.21.1", Encoding.UTF8.GetBytes(details));

        Assert.Equal(new Uri("https://piston-data.mojang.com/v1/objects/abc/server.jar"), info.JarUrl);
        Assert.Equal("abc", info.JarSha1);
        Assert.Equal(51_000_000, info.JarSize);
        Assert.Equal(21, info.JavaMajor);
    }

    [Fact]
    public void ParseServerInfo_WithoutJavaVersion_FallsBackToTable()
    {
        var details = SampleData.VersionDetails("https://piston-data.mojang.com/server.jar", "abc", 1, javaMajor: null);

        var info = MojangVersionService.ParseServerInfo("1.16.5", Encoding.UTF8.GetBytes(details));

        Assert.Equal(8, info.JavaMajor);
    }

    [Fact]
    public void ParseServerInfo_NoServerDownload_Throws()
    {
        var details = """{"id":"x","downloads":{"client":{"sha1":"a","size":1,"url":"https://example.com/c.jar"}}}""";

        Assert.Throws<InvalidDataException>(() => MojangVersionService.ParseServerInfo("x", Encoding.UTF8.GetBytes(details)));
    }
}
