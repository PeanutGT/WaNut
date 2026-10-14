using WaNut.Core.Downloads;
using WaNut.Core.Instances;
using WaNut.Core.Tests.TestSupport;
using WaNut.Core.Versions;

namespace WaNut.Core.Tests.Versions;

public sealed class ServerVersionCatalogTests
{
    private static ServerVersionCatalog Create()
    {
        var manifest = SampleData.Manifest(
            ("26.4-snapshot-4", "snapshot", "2026-10-13T00:00:00Z", "https://piston-meta.mojang.com/s.json", new string('0', 40)),
            ("26.3", "release", "2026-09-01T00:00:00Z", "https://piston-meta.mojang.com/263.json", new string('1', 40)),
            ("1.21.11", "release", "2025-12-01T00:00:00Z", "https://piston-meta.mojang.com/12111.json", new string('2', 40)),
            ("1.21.4", "release", "2024-12-03T00:00:00Z", "https://piston-meta.mojang.com/1214.json", new string('3', 40)),
            ("1.21.2", "release", "2024-10-22T00:00:00Z", "https://piston-meta.mojang.com/1212.json", new string('4', 40)),
            ("1.16.5", "release", "2021-01-14T00:00:00Z", "https://piston-meta.mojang.com/1165.json", new string('5', 40)),
            ("1.16.4", "release", "2020-11-02T00:00:00Z", "https://piston-meta.mojang.com/1164.json", new string('6', 40)));
        var http = new StubHttpHandler()
            .Add(MojangVersionService.ManifestUrl, manifest)
            .Add(PaperClient.ProjectUrl, SampleData.PaperProject)
            .Add(FabricClient.GameVersionsUrl, SampleData.FabricGameVersions);
        var client = http.CreateClient();
        return new ServerVersionCatalog(new MojangVersionService(client, new FileDownloader(client)), new PaperClient(client), new FabricClient(client));
    }

    [Theory]
    [InlineData(ServerType.Vanilla, new[] { "26.3", "1.21.11", "1.21.4", "1.21.2", "1.16.5" })]
    [InlineData(ServerType.Paper, new[] { "26.3", "1.21.11", "1.21.4", "1.16.5" })]
    [InlineData(ServerType.Fabric, new[] { "26.3", "1.21.4", "1.16.5" })]
    public async Task GetVersionsAsync_Type_OffersSupportedReleasesNewestFirst(ServerType type, string[] expected)
    {
        var versions = await Create().GetVersionsAsync(type);

        Assert.Equal(expected, versions.Select(v => v.Id));
    }
}
