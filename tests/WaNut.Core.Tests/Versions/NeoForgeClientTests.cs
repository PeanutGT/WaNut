using System.Text;
using WaNut.Core.Tests.TestSupport;
using WaNut.Core.Versions;

namespace WaNut.Core.Tests.Versions;

public sealed class NeoForgeClientTests
{
    [Theory]
    [InlineData("20.2.86", "1.20.2")]
    [InlineData("20.4.237", "1.20.4")]
    [InlineData("21.0.167", "1.21")]
    [InlineData("21.1.209", "1.21.1")]
    [InlineData("21.11.3-beta", "1.21.11")]
    [InlineData("26.1.0.5-beta", "26.1")]
    [InlineData("26.1.2.3", "26.1.2")]
    [InlineData("20.1.5", null)]
    [InlineData("0.25w14craftmine.3-beta", null)]
    [InlineData("21.1", null)]
    public void GameVersionOf_ReadsTheMinecraftVersion(string version, string? expected) =>
        Assert.Equal(expected, NeoForgeClient.GameVersionOf(version));

    [Fact]
    public void ParseBestVersions_PrefersNewestStableThenNewestBeta()
    {
        var json = """{"isSnapshot":false,"versions":["21.1.9","21.1.10","21.1.11-beta","21.4.0-beta","21.4.1-beta","20.4.80-beta","20.4.237"]}""";

        var best = NeoForgeClient.ParseBestVersions(new MemoryStream(Encoding.UTF8.GetBytes(json)));

        Assert.Equal("21.1.10", best["1.21.1"]);
        Assert.Equal("21.4.1-beta", best["1.21.4"]);
        Assert.Equal("20.4.237", best["1.20.4"]);
    }

    [Fact]
    public async Task GetInstallerAsync_ReturnsMavenUrlAndSha1()
    {
        var sha1 = new string('c', 40);
        var http = new StubHttpHandler()
            .Add(NeoForgeClient.VersionsUrl, """{"versions":["21.1.209"]}""")
            .Add(new Uri("https://maven.neoforged.net/releases/net/neoforged/neoforge/21.1.209/neoforge-21.1.209-installer.jar.sha1"), sha1);

        var installer = await new NeoForgeClient(http.CreateClient()).GetInstallerAsync("1.21.1");

        Assert.Equal(ServerLoader.NeoForge, installer.Loader);
        Assert.Equal("21.1.209", installer.LoaderVersion);
        Assert.Equal("21.1.209", installer.FullVersion);
        Assert.Equal(sha1, installer.Sha1);
    }

    [Fact]
    public async Task GetInstallerAsync_UnreadableChecksum_Throws()
    {
        var http = new StubHttpHandler()
            .Add(new Uri(NeoForgeClient.InstallerUrl("21.1.209").AbsoluteUri + ".sha1"), "<html>not found</html>");

        await Assert.ThrowsAsync<InvalidDataException>(() => new NeoForgeClient(http.CreateClient()).GetInstallerAsync("1.21.1", "21.1.209"));
    }
}
