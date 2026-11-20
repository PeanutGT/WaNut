using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using WaNut.Core.Downloads;
using WaNut.Core.Mods;
using WaNut.Core.Network;
using WaNut.Core.Packs;
using WaNut.Core.Tests.Mods;
using WaNut.Core.Tests.TestSupport;

namespace WaNut.Core.Tests.Packs;

public sealed class ResourcePackListTests : IDisposable
{
    private const string Game = "1.21.1";

    private readonly TempDir _dir = new();
    private readonly StubHttpHandler _http = new();
    private readonly PackLibrary _library;

    public ResourcePackListTests()
    {
        var client = _http.CreateClient();
        _library = new PackLibrary(new ModrinthClient(client), new FileDownloader(client));
        File.WriteAllText(Path.Combine(_dir.Path, "server.properties"), "level-name=world\n");
    }

    public void Dispose() => _dir.Dispose();

    private void AddPack(string projectId, string sharedFile, string content)
    {
        byte[] zip;
        using (var stream = new MemoryStream())
        {
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var (path, text) in new[] { ("pack.mcmeta", projectId), (sharedFile, content) })
                {
                    using var writer = new StreamWriter(archive.CreateEntry(path).Open());
                    writer.Write(text);
                }
            }

            zip = stream.ToArray();
        }

        var url = new Uri($"https://cdn.modrinth.com/data/{projectId}/{projectId}.zip");
        var sha512 = Convert.ToHexStringLower(System.Security.Cryptography.SHA512.HashData(zip));
        var version = ModrinthClientTests.Version(projectId + "-v", "release", "2026-10-01T00:00:00Z", fileName: projectId + ".zip", url: url.AbsoluteUri,
            sha512: sha512, loader: "minecraft").Replace("\"project_id\":\"proj\"", $"\"project_id\":\"{projectId}\"", StringComparison.Ordinal);
        _http.Add(ModrinthClient.PackVersionsUrl(projectId, PackKind.ResourcePack, Game), $"[{version}]").Add(url, zip);
    }

    private string Properties => File.ReadAllText(Path.Combine(_dir.Path, "server.properties"));

    [Fact]
    public async Task AddResourcePackAsync_OnePack_UsesModrinthUrlAndNoMergedZip()
    {
        AddPack("a", "assets/x.txt", "a");

        var change = await _library.AddResourcePackAsync(_dir.Path, Game, "a", "Pack A");

        Assert.True(change.Changed);
        Assert.Contains("resource-pack=https\\://cdn.modrinth.com/data/a/a.zip", Properties, StringComparison.Ordinal);
        Assert.Null(_library.GetMergedPackPath(_dir.Path));
        Assert.Equal("Pack A", _library.GetResourcePack(_dir.Path)!.Title);
    }

    [Fact]
    public async Task AddResourcePackAsync_SecondPack_MergesAndWritesMergedSha1()
    {
        AddPack("a", "assets/x.txt", "from-a");
        AddPack("b", "assets/x.txt", "from-b");
        await _library.AddResourcePackAsync(_dir.Path, Game, "a", "Pack A");

        var change = await _library.AddResourcePackAsync(_dir.Path, Game, "b", "Pack B");

        var merged = _library.GetMergedPackPath(_dir.Path)!;
        Assert.Single(change.Overlaps);
        using (var archive = ZipFile.OpenRead(merged))
        {
            using var reader = new StreamReader(archive.GetEntry("assets/x.txt")!.Open());
            Assert.Equal("from-a", reader.ReadToEnd());
        }

        var sha1 = Convert.ToHexStringLower(System.Security.Cryptography.SHA1.HashData(File.ReadAllBytes(merged)));
        Assert.Contains($"resource-pack-sha1={sha1}", Properties, StringComparison.Ordinal);
        Assert.Equal(["a", "b"], _library.ListResourcePacks(_dir.Path).Select(p => p.ProjectId));
    }

    [Fact]
    public async Task AddResourcePackAsync_SameProjectTwice_DoesNothing()
    {
        AddPack("a", "assets/x.txt", "a");
        await _library.AddResourcePackAsync(_dir.Path, Game, "a", "Pack A");

        var again = await _library.AddResourcePackAsync(_dir.Path, Game, "a", "Pack A");

        Assert.False(again.Changed);
    }

    [Fact]
    public async Task AddResourcePackAsync_AfterSingleModrinthPack_KeepsOldPackOnTop()
    {
        AddPack("old", "assets/x.txt", "old");
        AddPack("new", "assets/x.txt", "new");
        await _library.SetResourcePackAsync(_dir.Path, Game, "old", "Old", required: false);

        await _library.AddResourcePackAsync(_dir.Path, Game, "new", "New");

        Assert.Equal(["old", "new"], _library.ListResourcePacks(_dir.Path).Select(p => p.ProjectId));
    }

    [Fact]
    public async Task MoveResourcePack_ChangesWinnerAndMergedSha1()
    {
        AddPack("a", "assets/x.txt", "from-a");
        AddPack("b", "assets/x.txt", "from-b");
        await _library.AddResourcePackAsync(_dir.Path, Game, "a", "Pack A");
        await _library.AddResourcePackAsync(_dir.Path, Game, "b", "Pack B");
        var before = Properties;

        var change = _library.MoveResourcePack(_dir.Path, _library.ListResourcePacks(_dir.Path)[1].FileName, 0);

        Assert.True(change.Changed);
        Assert.Equal(["b", "a"], _library.ListResourcePacks(_dir.Path).Select(p => p.ProjectId));
        Assert.NotEqual(before, Properties);
    }

    [Fact]
    public async Task RemoveResourcePack_DownToOne_DropsMergedZipAndUsesModrinthUrl()
    {
        AddPack("a", "assets/x.txt", "a");
        AddPack("b", "assets/x.txt", "b");
        await _library.AddResourcePackAsync(_dir.Path, Game, "a", "Pack A");
        await _library.AddResourcePackAsync(_dir.Path, Game, "b", "Pack B");

        _library.RemoveResourcePack(_dir.Path, _library.ListResourcePacks(_dir.Path)[0].FileName);

        Assert.Null(_library.GetMergedPackPath(_dir.Path));
        Assert.False(File.Exists(PackLibrary.MergedPackPath(_dir.Path)));
        Assert.Contains("cdn.modrinth.com/data/b/b.zip", Properties, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClearResourcePack_RemovesListZipsAndMergedFile()
    {
        AddPack("a", "assets/x.txt", "a");
        AddPack("b", "assets/x.txt", "b");
        await _library.AddResourcePackAsync(_dir.Path, Game, "a", "Pack A");
        await _library.AddResourcePackAsync(_dir.Path, Game, "b", "Pack B");

        _library.ClearResourcePack(_dir.Path);

        Assert.Empty(_library.ListResourcePacks(_dir.Path));
        Assert.Empty(Directory.GetFiles(PackLibrary.ResourcePacksDir(_dir.Path)));
        Assert.Null(_library.GetResourcePack(_dir.Path));
    }

    [Fact]
    public async Task PointResourcePackAt_ChangesOnlyTheUrl()
    {
        AddPack("a", "assets/x.txt", "a");
        AddPack("b", "assets/x.txt", "b");
        await _library.AddResourcePackAsync(_dir.Path, Game, "a", "Pack A");
        await _library.AddResourcePackAsync(_dir.Path, Game, "b", "Pack B");

        _library.PointResourcePackAt(_dir.Path, new Uri("http://10.0.0.5:5000/resourcepack.zip"));

        var pack = _library.GetResourcePack(_dir.Path)!;
        Assert.Equal("http://10.0.0.5:5000/resourcepack.zip", pack.Url);
        Assert.NotNull(pack.Sha1);
    }

    [Fact]
    public async Task ResourcePackHost_ServesTheZipOnItsPort()
    {
        var path = Path.Combine(_dir.Path, "p.zip");
        var bytes = new byte[300_000];
        new Random(1).NextBytes(bytes);
        File.WriteAllBytes(path, bytes);

        await using var host = ResourcePackHost.Start(path);
        using var client = new HttpClient();
        var body = await client.GetByteArrayAsync(host.UrlFor(IPAddress.Loopback));

        Assert.Equal(bytes, body);
    }

    [Fact]
    public async Task ResourcePackHost_Disposed_StopsListening()
    {
        var path = Path.Combine(_dir.Path, "p.zip");
        File.WriteAllBytes(path, [1]);
        var host = ResourcePackHost.Start(path);
        var port = host.Port;

        await host.DisposeAsync();

        using var tcp = new TcpClient();
        await Assert.ThrowsAnyAsync<SocketException>(() => tcp.ConnectAsync(IPAddress.Loopback, port));
    }

    [Fact]
    public void ResourcePackAddress_PrefersVirtualLanOverHomeNetwork()
    {
        var lan = new LocalAddress("Wi-Fi", AdapterKind.Lan, IPAddress.Parse("192.168.1.5"));
        var radmin = new LocalAddress("Radmin VPN", AdapterKind.RadminVpn, IPAddress.Parse("26.1.2.3"));

        Assert.Equal(radmin.Address, ResourcePackAddress.Pick([lan, radmin]));
        Assert.Equal(lan.Address, ResourcePackAddress.Pick([lan]));
        Assert.Null(ResourcePackAddress.Pick([]));
    }

    private string MakeLocalZip(string fileName, bool withMcmeta = true, string content = "local")
    {
        var path = Path.Combine(_dir.Path, "incoming-" + fileName);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(archive.CreateEntry(withMcmeta ? "pack.mcmeta" : "readme.txt").Open()))
        {
            writer.Write(content);
        }

        return path;
    }

    [Fact]
    public async Task AddLocalResourcePackAsync_OnlyPack_IsMergedZipWithItsSha1AndNoUrl()
    {
        var zip = MakeLocalZip("My Pack.zip");

        var change = await _library.AddLocalResourcePackAsync(_dir.Path, Game, zip);

        Assert.True(change.Changed);
        var entry = Assert.Single(_library.ListResourcePacks(_dir.Path));
        Assert.Equal("incoming-My Pack", entry.Title);
        Assert.True(PackLibrary.IsLocalPack(entry));
        Assert.True(File.Exists(Path.Combine(PackLibrary.ResourcePacksDir(_dir.Path), entry.FileName)));
        var merged = _library.GetMergedPackPath(_dir.Path)!;
        Assert.NotNull(merged);
        Assert.Contains($"resource-pack-sha1={ResourcePackLink.Sha1Of(merged)}", Properties, StringComparison.Ordinal);
        Assert.DoesNotContain("modrinth", Properties, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddLocalResourcePackAsync_SameFileTwice_DoesNothing()
    {
        var zip = MakeLocalZip("a.zip");
        await _library.AddLocalResourcePackAsync(_dir.Path, Game, zip);

        var again = await _library.AddLocalResourcePackAsync(_dir.Path, Game, zip);

        Assert.False(again.Changed);
        Assert.Single(_library.ListResourcePacks(_dir.Path));
    }

    [Fact]
    public async Task AddLocalResourcePackAsync_NoMcmeta_ThrowsAndChangesNothing()
    {
        var zip = MakeLocalZip("bad.zip", withMcmeta: false);

        await Assert.ThrowsAsync<InvalidDataException>(() => _library.AddLocalResourcePackAsync(_dir.Path, Game, zip));

        Assert.Empty(_library.ListResourcePacks(_dir.Path));
        Assert.Null(_library.GetMergedPackPath(_dir.Path));
    }

    [Fact]
    public async Task AddLocalResourcePackAsync_NotAZip_Throws()
    {
        var path = Path.Combine(_dir.Path, "text.zip");
        File.WriteAllText(path, "hello");

        await Assert.ThrowsAsync<InvalidDataException>(() => _library.AddLocalResourcePackAsync(_dir.Path, Game, path));
    }

    [Fact]
    public async Task AddLocalResourcePackAsync_AfterModrinthPack_Merges()
    {
        AddPack("a", "assets/x.txt", "from-a");
        await _library.AddResourcePackAsync(_dir.Path, Game, "a", "Pack A");

        await _library.AddLocalResourcePackAsync(_dir.Path, Game, MakeLocalZip("mine.zip"));

        Assert.Equal(2, _library.ListResourcePacks(_dir.Path).Count);
        Assert.NotNull(_library.GetMergedPackPath(_dir.Path));
    }

    [Fact]
    public async Task RemoveResourcePack_LocalPackLeavingModrinthPack_GoesBackToItsUrl()
    {
        AddPack("a", "assets/x.txt", "from-a");
        await _library.AddResourcePackAsync(_dir.Path, Game, "a", "Pack A");
        await _library.AddLocalResourcePackAsync(_dir.Path, Game, MakeLocalZip("mine.zip"));
        var local = _library.ListResourcePacks(_dir.Path).Single(PackLibrary.IsLocalPack);

        _library.RemoveResourcePack(_dir.Path, local.FileName);

        Assert.Null(_library.GetMergedPackPath(_dir.Path));
        Assert.Contains("resource-pack=https\\://cdn.modrinth.com/data/a/a.zip", Properties, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemoveResourcePack_LastLocalPack_ClearsProperties()
    {
        await _library.AddLocalResourcePackAsync(_dir.Path, Game, MakeLocalZip("mine.zip"));
        var local = _library.ListResourcePacks(_dir.Path).Single();

        _library.RemoveResourcePack(_dir.Path, local.FileName);

        Assert.Empty(_library.ListResourcePacks(_dir.Path));
        Assert.Null(_library.GetMergedPackPath(_dir.Path));
        Assert.Null(_library.GetResourcePack(_dir.Path));
    }
}
