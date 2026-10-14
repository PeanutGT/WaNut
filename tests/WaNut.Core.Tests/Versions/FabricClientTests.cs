using System.IO.Compression;
using System.Text;
using WaNut.Core.Tests.TestSupport;
using WaNut.Core.Versions;

namespace WaNut.Core.Tests.Versions;

public sealed class FabricClientTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private static MemoryStream Json(string text) => new(Encoding.UTF8.GetBytes(text));

    [Fact]
    public void ParseStableGameVersions_List_SkipsSnapshotsAndCandidates()
    {
        var ids = FabricClient.ParseStableGameVersions(Json(SampleData.FabricGameVersions));

        Assert.Equal(["1.16.5", "1.21.4", "26.3"], ids.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ParseLatestStable_NewestIsBeta_SkipsIt()
    {
        Assert.Equal("0.19.5", FabricClient.ParseLatestStable(Json(SampleData.FabricLoaderVersions), "loader"));
    }

    [Theory]
    [InlineData("6af8ea8dcddec6fbdf1df8a8efd5a0d2bc6eb8a34656dbbe024dfe8e0c27274b")]
    [InlineData("6AF8EA8DCDDEC6FBDF1DF8A8EFD5A0D2BC6EB8A34656DBBE024DFE8E0C27274B  fabric-installer-1.1.2-server.jar\n")]
    public void ParseChecksumFile_MavenFormats_ReturnsLowercaseHex(string text)
    {
        Assert.Equal("6af8ea8dcddec6fbdf1df8a8efd5a0d2bc6eb8a34656dbbe024dfe8e0c27274b", FabricClient.ParseChecksumFile(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html>Not found</html>")]
    [InlineData("74685b5a0178c4dabd4be741a276d35c59b5ec96")]
    public void ParseChecksumFile_NotASha256_Throws(string text)
    {
        Assert.Throws<InvalidDataException>(() => FabricClient.ParseChecksumFile(text));
    }

    [Fact]
    public void ServerLauncherUrl_Version_PointsAtMavenServerJar()
    {
        Assert.Equal(
            "https://maven.fabricmc.net/net/fabricmc/fabric-installer/1.1.2/fabric-installer-1.1.2-server.jar",
            FabricClient.ServerLauncherUrl("1.1.2").AbsoluteUri);
    }

    [Fact]
    public async Task GetLatestLauncherAsync_Meta_UsesStableVersionsAndMavenChecksum()
    {
        var jarUrl = FabricClient.ServerLauncherUrl("1.1.2");
        var http = new StubHttpHandler()
            .Add(FabricClient.LoaderVersionsUrl, SampleData.FabricLoaderVersions)
            .Add(FabricClient.InstallerVersionsUrl, SampleData.FabricInstallerVersions)
            .Add(new Uri(jarUrl.AbsoluteUri + ".sha256"), new string('d', 64));

        var launcher = await new FabricClient(http.CreateClient()).GetLatestLauncherAsync();

        Assert.Equal(new FabricLauncher("0.19.5", "1.1.2", jarUrl, new string('d', 64)), launcher);
    }

    [Fact]
    public void WriteInstallProperties_LauncherJar_AddsLoaderAndGameVersion()
    {
        var jar = Path.Combine(_temp.Path, "launch.jar");
        File.WriteAllBytes(jar, SampleData.FabricServerLauncherJar());

        FabricClient.WriteInstallProperties(jar, "0.19.5", "1.21.4");
        FabricClient.WriteInstallProperties(jar, "0.19.5", "1.21.4");

        using var zip = ZipFile.OpenRead(jar);
        var entry = Assert.Single(zip.Entries, e => e.FullName == "install.properties");
        using var reader = new StreamReader(entry.Open());
        Assert.Equal("fabric-loader-version=0.19.5\ngame-version=1.21.4\n", reader.ReadToEnd());
        Assert.NotNull(zip.GetEntry("META-INF/MANIFEST.MF"));
    }
}
