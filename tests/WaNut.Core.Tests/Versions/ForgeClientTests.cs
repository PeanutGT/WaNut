using System.Text;
using WaNut.Core.Tests.TestSupport;
using WaNut.Core.Versions;

namespace WaNut.Core.Tests.Versions;

public sealed class ForgeClientTests
{
    private const string Promotions = """
        {"homepage":"https://files.minecraftforge.net/","promos":{
          "1.16.5-latest":"36.2.42","1.16.5-recommended":"36.2.34",
          "1.21.4-latest":"54.1.0",
          "1.7.10_pre4-latest":"10.12.2.1149",
          "broken":"1.0","1.20.1-latest":null}}
        """;

    private static IReadOnlyDictionary<string, string> Parse(string json) =>
        ForgeClient.ParsePromotions(new MemoryStream(Encoding.UTF8.GetBytes(json)));

    [Fact]
    public void ParsePromotions_PrefersRecommendedThenLatest()
    {
        var promoted = Parse(Promotions);

        Assert.Equal("36.2.34", promoted["1.16.5"]);
        Assert.Equal("54.1.0", promoted["1.21.4"]);
        Assert.False(promoted.ContainsKey("1.20.1"));
        Assert.False(promoted.ContainsKey("broken"));
    }

    [Fact]
    public void ParsePromotions_NoPromos_Throws() =>
        Assert.Throws<InvalidDataException>(() => Parse("""{"homepage":"x"}"""));

    [Fact]
    public async Task GetInstallerAsync_PromotedVersion_ReturnsMavenUrlAndSha1()
    {
        var sha1 = new string('a', 40);
        var http = new StubHttpHandler()
            .Add(ForgeClient.PromotionsUrl, Promotions)
            .Add(new Uri("https://maven.minecraftforge.net/net/minecraftforge/forge/1.16.5-36.2.34/forge-1.16.5-36.2.34-installer.jar.sha1"), sha1.ToUpperInvariant() + "\n");

        var installer = await new ForgeClient(http.CreateClient()).GetInstallerAsync("1.16.5");

        Assert.Equal(ServerLoader.Forge, installer.Loader);
        Assert.Equal("36.2.34", installer.LoaderVersion);
        Assert.Equal("1.16.5-36.2.34", installer.FullVersion);
        Assert.Equal("https://maven.minecraftforge.net/net/minecraftforge/forge/1.16.5-36.2.34/forge-1.16.5-36.2.34-installer.jar", installer.JarUrl.AbsoluteUri);
        Assert.Equal(sha1, installer.Sha1);
    }

    [Fact]
    public async Task GetInstallerAsync_PinnedVersion_SkipsThePromotionsList()
    {
        var http = new StubHttpHandler()
            .Add(new Uri(ForgeClient.InstallerUrl("1.20.1-47.2.0").AbsoluteUri + ".sha1"), new string('b', 40));

        var installer = await new ForgeClient(http.CreateClient()).GetInstallerAsync("1.20.1", "47.2.0");

        Assert.Equal("1.20.1-47.2.0", installer.FullVersion);
        Assert.DoesNotContain(ForgeClient.PromotionsUrl, http.Requests);
    }

    [Fact]
    public async Task GetInstallerAsync_VersionWithoutForge_Throws()
    {
        var http = new StubHttpHandler().Add(ForgeClient.PromotionsUrl, Promotions);

        await Assert.ThrowsAsync<InvalidDataException>(() => new ForgeClient(http.CreateClient()).GetInstallerAsync("26.3"));
    }
}
