using System.Text;
using WaNut.Core.Versions;

namespace WaNut.Core.Tests.Versions;

public sealed class PaperClientTests
{
    private static MemoryStream Json(string text) => new(Encoding.UTF8.GetBytes(text));

    [Fact]
    public void ParseVersionIds_Project_KeepsReleasesFromEveryFamily()
    {
        var ids = PaperClient.ParseVersionIds(Json(TestSupport.SampleData.PaperProject));

        Assert.Equal(["1.16.4", "1.16.5", "1.21.1", "1.21.11", "1.21.4", "26.3"], ids.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ParseBestBuild_NewestIsAlpha_PicksNewestStable()
    {
        var json = TestSupport.SampleData.PaperBuilds(
            (12, "ALPHA", "https://fill-data.papermc.io/v1/objects/c/paper-12.jar", new string('c', 64)),
            (11, "STABLE", "https://fill-data.papermc.io/v1/objects/b/paper-11.jar", new string('b', 64)),
            (10, "STABLE", "https://fill-data.papermc.io/v1/objects/a/paper-10.jar", new string('a', 64)));

        var build = PaperClient.ParseBestBuild("1.21.4", Json(json));

        Assert.Equal(11, build.Id);
        Assert.Equal("STABLE", build.Channel);
        Assert.Equal(new string('b', 64), build.Sha256);
        Assert.Equal("https://fill-data.papermc.io/v1/objects/b/paper-11.jar", build.JarUrl.AbsoluteUri);
    }

    [Fact]
    public void ParseBestBuild_NoStableBuild_PicksNewestBeta()
    {
        var json = TestSupport.SampleData.PaperBuilds(
            (3, "ALPHA", "https://fill-data.papermc.io/3.jar", new string('c', 64)),
            (2, "BETA", "https://fill-data.papermc.io/2.jar", new string('b', 64)),
            (1, "ALPHA", "https://fill-data.papermc.io/1.jar", new string('a', 64)));

        Assert.Equal(2, PaperClient.ParseBestBuild("26.4", Json(json)).Id);
    }

    [Fact]
    public void ParseBestBuild_HttpDownload_IsRejected()
    {
        var json = TestSupport.SampleData.PaperBuilds((1, "STABLE", "http://fill-data.papermc.io/1.jar", new string('a', 64)));

        Assert.Throws<InvalidDataException>(() => PaperClient.ParseBestBuild("1.21.4", Json(json)));
    }

    [Fact]
    public void ParseBestBuild_NoBuilds_Throws()
    {
        Assert.Throws<InvalidDataException>(() => PaperClient.ParseBestBuild("1.21.4", Json("[]")));
    }

    [Fact]
    public void BuildsUrl_Version_PointsAtFillV3()
    {
        Assert.Equal("https://fill.papermc.io/v3/projects/paper/versions/1.21.4/builds", PaperClient.BuildsUrl("1.21.4").AbsoluteUri);
    }
}
