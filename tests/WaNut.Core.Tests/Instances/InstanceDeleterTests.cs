using System.IO.Compression;
using WaNut.Core.Backups;
using WaNut.Core.Instances;
using WaNut.Core.Tests.TestSupport;
using WaNut.Core.Worlds;

namespace WaNut.Core.Tests.Instances;

public sealed class InstanceDeleterTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero));
    private readonly LauncherPaths _paths;
    private readonly InstanceStore _store;
    private readonly InstanceDeleter _deleter;

    public InstanceDeleterTests()
    {
        _paths = new LauncherPaths(_temp.Path);
        _store = new InstanceStore(_paths);
        _deleter = new InstanceDeleter(_paths, _store, new WorldExporter(new BackupStore(_clock), _clock), _clock);
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task DeleteAsync_KeepWorld_ZipsWorldThenRemovesFolder()
    {
        var server = NewServer("My Server", withWorld: true);

        var zip = await _deleter.DeleteAsync(server, keepWorld: true);

        Assert.False(Directory.Exists(_store.InstanceDir(server.Id)));
        Assert.Empty(_store.LoadAll());
        Assert.Equal(Path.Combine(_paths.DeletedServersDir, "My Server 2026-10-16 12-00.zip"), zip);
        using var archive = ZipFile.OpenRead(zip!);
        Assert.Contains(archive.Entries, e => e.FullName.EndsWith("level.dat", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DeleteAsync_DeleteEverything_KeepsNoZip()
    {
        var server = NewServer("My Server", withWorld: true);

        var zip = await _deleter.DeleteAsync(server, keepWorld: false);

        Assert.Null(zip);
        Assert.False(Directory.Exists(_paths.DeletedServersDir));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_paths.InstancesDir));
    }

    [Fact]
    public async Task DeleteAsync_NoWorldYet_DeletesWithoutZip()
    {
        var server = NewServer("Fresh", withWorld: false);

        Assert.Null(await _deleter.DeleteAsync(server, keepWorld: true));
        Assert.False(Directory.Exists(_store.InstanceDir(server.Id)));
    }

    [Fact]
    public async Task DeleteAsync_SameNameTwiceInOneMinute_KeepsBothZips()
    {
        var first = await _deleter.DeleteAsync(NewServer("Twin", withWorld: true), keepWorld: true);
        var second = await _deleter.DeleteAsync(NewServer("Twin", withWorld: true), keepWorld: true);

        Assert.NotEqual(first, second);
        Assert.True(File.Exists(first));
        Assert.True(File.Exists(second));
    }

    [Fact]
    public async Task DeleteAsync_OtherServers_AreKept()
    {
        var keep = NewServer("Keep", withWorld: true);
        var server = NewServer("Gone", withWorld: true);

        await _deleter.DeleteAsync(server, keepWorld: false);

        Assert.Equal("Keep", Assert.Single(_store.LoadAll()).Name);
        Assert.True(InstanceDeleter.HasWorld(_store.InstanceDir(keep.Id)));
    }

    [Fact]
    public async Task DeleteAsync_ReadOnlyFile_IsDeleted()
    {
        var server = NewServer("Locked", withWorld: true);
        var file = Path.Combine(_store.InstanceDir(server.Id), "read-only.txt");
        File.WriteAllText(file, "x");
        File.SetAttributes(file, FileAttributes.ReadOnly);

        await _deleter.DeleteAsync(server, keepWorld: false);

        Assert.False(Directory.Exists(_store.InstanceDir(server.Id)));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../outside")]
    [InlineData("")]
    [InlineData(".wanut-deleting-x")]
    public async Task DeleteAsync_IdOutsideServersFolder_Throws(string id)
    {
        var outside = Path.Combine(_temp.Path, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, InstanceMetadata.FileName), "{}");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _deleter.DeleteAsync(Sample(id, "x"), keepWorld: false));
        Assert.True(Directory.Exists(outside));
    }

    [Fact]
    public async Task DeleteAsync_FolderWithoutInstanceJson_Throws()
    {
        var dir = _store.InstanceDir("not-a-server");
        Directory.CreateDirectory(dir);

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => _deleter.DeleteAsync(Sample("not-a-server", "x"), keepWorld: false));
        Assert.True(Directory.Exists(dir));
    }

    [Fact]
    public async Task DeleteAsync_LeftoverFromEarlierDelete_IsCleanedUpAndNeverListed()
    {
        var leftover = Path.Combine(_paths.InstancesDir, ".wanut-deleting-old");
        _store.Save(Sample("old", "Old"));
        Directory.Move(_store.InstanceDir("old"), leftover);

        Assert.Empty(_store.LoadAll());

        await _deleter.DeleteAsync(NewServer("Other", withWorld: false), keepWorld: false);

        Assert.False(Directory.Exists(leftover));
    }

    private InstanceMetadata NewServer(string name, bool withWorld)
    {
        var server = Sample(_store.CreateFolder(name), name);
        _store.Save(server);
        if (withWorld)
        {
            WorldFixture.Create(Path.Combine(_store.InstanceDir(server.Id), "world"), name);
        }

        return server;
    }

    private static InstanceMetadata Sample(string id, string name) => new()
    {
        Id = id,
        Name = name,
        Type = ServerType.Vanilla,
        MinecraftVersion = "1.21.1",
        JavaMajor = 21,
        MemoryMb = 4096,
        CreatedAt = DateTimeOffset.UnixEpoch,
    };
}
