using System.Formats.Tar;
using System.IO.Compression;
using WaNut.Core.Backups;
using WaNut.Core.Storage;
using WaNut.Core.Tests.TestSupport;

namespace WaNut.Core.Tests.Backups;

public sealed class ServerFolderArchiveTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public async Task CreateAsync_PacksWorldsAndModsIntoBackups()
    {
        await using var files = new LocalFileSystemAdapter(_dir.Path);
        await files.WriteAllTextAsync("world/level.dat", "w");
        await files.WriteAllTextAsync("world_nether/level.dat", "n");
        await files.WriteAllTextAsync("logs/latest.log", "l");
        await files.WriteAllTextAsync("mods/a.jar", "a");

        var archive = await ServerFolderArchive.CreateAsync(files, "mod-update", ["mods", "plugins"], new DateTime(2026, 11, 28, 17, 0, 5, DateTimeKind.Utc));

        Assert.Equal("backups/2026-11-28_17-00-05-mod-update.tar.gz", archive);
        Assert.Equal(["mods/a.jar", "world/level.dat", "world_nether/level.dat"], ReadTarFiles(Path.Combine(_dir.Path, archive!)));
    }

    [Fact]
    public async Task CreateAsync_NothingToPack_ReturnsNull()
    {
        await using var files = new LocalFileSystemAdapter(_dir.Path);
        await files.WriteAllTextAsync("server.properties", "x");

        Assert.Null(await ServerFolderArchive.CreateAsync(files, "mod-update", ["mods"], DateTime.UtcNow));
    }

    private static List<string> ReadTarFiles(string path)
    {
        using var file = File.OpenRead(path);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var tar = new TarReader(gzip);
        var names = new List<string>();
        while (tar.GetNextEntry() is { } entry)
        {
            names.Add(entry.Name);
        }

        return [.. names.Order(StringComparer.Ordinal)];
    }
}
