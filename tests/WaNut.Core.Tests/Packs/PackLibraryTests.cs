using System.Text;
using WaNut.Core.Downloads;
using WaNut.Core.Mods;
using WaNut.Core.Packs;
using WaNut.Core.Tests.Mods;
using WaNut.Core.Tests.TestSupport;

namespace WaNut.Core.Tests.Packs;

public sealed class PackLibraryTests : IDisposable
{
    private const string Game = "1.21.1";

    private readonly TempDir _dir = new();
    private readonly StubHttpHandler _http = new();
    private readonly PackLibrary _library;

    public PackLibraryTests()
    {
        var client = _http.CreateClient();
        _library = new PackLibrary(new ModrinthClient(client), new FileDownloader(client));
    }

    public void Dispose() => _dir.Dispose();

    private static string Sha512(byte[] data) => Convert.ToHexStringLower(System.Security.Cryptography.SHA512.HashData(data));

    private void AddPack(string projectId, PackKind kind, byte[] zip, string loader)
    {
        var url = new Uri($"https://cdn.modrinth.com/data/{projectId}/{projectId}.zip");
        var version = ModrinthClientTests.Version(projectId + "-v", "release", "2026-10-01T00:00:00Z", fileName: projectId + ".zip", url: url.AbsoluteUri,
            sha512: Sha512(zip), loader: loader).Replace("\"project_id\":\"proj\"", $"\"project_id\":\"{projectId}\"", StringComparison.Ordinal);
        _http.Add(ModrinthClient.PackVersionsUrl(projectId, kind, Game), $"[{version}]").Add(url, zip);
    }

    [Fact]
    public async Task InstallDatapackAsync_PutsZipInLevelFolderAndNamesIt()
    {
        File.WriteAllText(Path.Combine(_dir.Path, "server.properties"), "level-name=myworld\n");
        AddPack("tweaks", PackKind.DataPack, Encoding.UTF8.GetBytes("zip"), "datapack");

        Assert.True(await _library.InstallDatapackAsync(_dir.Path, Game, "tweaks", "Vanilla Tweaks"));
        Assert.False(await _library.InstallDatapackAsync(_dir.Path, Game, "tweaks", "Vanilla Tweaks"));

        Assert.True(File.Exists(Path.Combine(_dir.Path, "myworld", "datapacks", "tweaks.zip")));
        var pack = Assert.Single(_library.ListDatapacks(_dir.Path));
        Assert.Equal("Vanilla Tweaks", pack.DisplayName);
    }

    [Fact]
    public async Task RemoveDatapack_DeletesZipAndHandMadeFolder()
    {
        var dir = PackLibrary.DatapacksDir(_dir.Path);
        Directory.CreateDirectory(Path.Combine(dir, "my-pack"));
        File.WriteAllText(Path.Combine(dir, "my-pack", "pack.mcmeta"), "{}");

        _library.RemoveDatapack(_dir.Path, Assert.Single(_library.ListDatapacks(_dir.Path)));

        Assert.Empty(_library.ListDatapacks(_dir.Path));
        await Task.CompletedTask;
    }

    [Fact]
    public void DatapacksDir_UnsafeLevelName_Throws()
    {
        File.WriteAllText(Path.Combine(_dir.Path, "server.properties"), "level-name=../other\n");

        Assert.Throws<InvalidDataException>(() => PackLibrary.DatapacksDir(_dir.Path));
    }

    [Fact]
    public async Task SetResourcePackAsync_WritesUrlSha1AndRequire_ThenClears()
    {
        File.WriteAllText(Path.Combine(_dir.Path, "server.properties"), "motd=hi\nresource-pack=\n");
        AddPack("fresh", PackKind.ResourcePack, Encoding.UTF8.GetBytes("rp"), "minecraft");

        await _library.SetResourcePackAsync(_dir.Path, Game, "fresh", "Fresh Animations", required: true);

        var properties = File.ReadAllText(Path.Combine(_dir.Path, "server.properties"));
        Assert.Contains("resource-pack-sha1=x", properties, StringComparison.Ordinal);
        Assert.Contains("require-resource-pack=true", properties, StringComparison.Ordinal);
        Assert.Contains("motd=hi", properties, StringComparison.Ordinal);
        var pack = _library.GetResourcePack(_dir.Path);
        Assert.Equal(new ServerResourcePack("https://cdn.modrinth.com/data/fresh/fresh.zip", "x", true, "Fresh Animations"), pack);

        _library.SetResourcePackRequired(_dir.Path, false);
        Assert.False(_library.GetResourcePack(_dir.Path)!.Required);

        _library.ClearResourcePack(_dir.Path);
        Assert.Null(_library.GetResourcePack(_dir.Path));
    }

    [Fact]
    public async Task SetResourcePackAsync_NoVersion_Throws()
    {
        _http.Add(ModrinthClient.PackVersionsUrl("none", PackKind.ResourcePack, Game), "[]");

        await Assert.ThrowsAsync<NoCompatibleVersionException>(() => _library.SetResourcePackAsync(_dir.Path, Game, "none", "None", required: false));
    }

    [Fact]
    public void PackSearchUrl_DataPacks_UsesDatapackType()
    {
        var url = ModrinthClient.PackSearchUrl(PackKind.DataPack, Game, string.Empty, 0);

        Assert.Contains(Uri.EscapeDataString("project_type:datapack"), url.Query, StringComparison.Ordinal);
        Assert.Contains("index=downloads", url.Query, StringComparison.Ordinal);
    }
}
