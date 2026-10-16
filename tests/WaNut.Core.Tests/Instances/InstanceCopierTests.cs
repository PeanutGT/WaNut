using WaNut.Core.Instances;
using WaNut.Core.Tests.TestSupport;

namespace WaNut.Core.Tests.Instances;

public sealed class InstanceCopierTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero));
    private readonly InstanceStore _store;
    private readonly InstanceCopier _copier;

    public InstanceCopierTests()
    {
        _store = new InstanceStore(new LauncherPaths(_temp.Path));
        _copier = new InstanceCopier(_store, _clock);
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task CopyAsync_Server_CopiesWorldSettingsAndMods()
    {
        var source = NewServer("Survival");
        var dir = _store.InstanceDir(source.Id);
        Write(dir, "server.properties", "motd=hi");
        Write(dir, "world/level.dat", "level");
        Write(dir, "mods/lithium.jar", "jar");

        var copy = await _copier.CopyAsync(source, "Survival copy");

        var copyDir = _store.InstanceDir(copy.Id);
        Assert.Equal("survival-copy", copy.Id);
        Assert.Equal("Survival copy", copy.Name);
        Assert.Equal(source.MinecraftVersion, copy.MinecraftVersion);
        Assert.Equal(_clock.GetUtcNow(), copy.CreatedAt);
        Assert.Equal("motd=hi", File.ReadAllText(Path.Combine(copyDir, "server.properties")));
        Assert.True(File.Exists(Path.Combine(copyDir, "world", "level.dat")));
        Assert.True(File.Exists(Path.Combine(copyDir, "mods", "lithium.jar")));
        Assert.Equal(2, _store.LoadAll().Count);
    }

    [Fact]
    public async Task CopyAsync_Server_LeavesOutBackupsLogsAndSessionLock()
    {
        var source = NewServer("Survival");
        var dir = _store.InstanceDir(source.Id);
        Write(dir, "backups/2026-10-16_12-00-00_start.zip", "zip");
        Write(dir, "logs/latest.log", "log");
        Write(dir, "crash-reports/crash.txt", "crash");
        Write(dir, "world/session.lock", "lock");
        Write(dir, ".wanut-restore-new-1/world/level.dat", "staged");

        var copy = await _copier.CopyAsync(source, "Copy");

        var copyDir = _store.InstanceDir(copy.Id);
        Assert.False(Directory.Exists(Path.Combine(copyDir, "backups")));
        Assert.False(Directory.Exists(Path.Combine(copyDir, "logs")));
        Assert.False(Directory.Exists(Path.Combine(copyDir, "crash-reports")));
        Assert.False(File.Exists(Path.Combine(copyDir, "world", "session.lock")));
        Assert.False(Directory.Exists(Path.Combine(copyDir, ".wanut-restore-new-1")));
    }

    [Fact]
    public async Task CopyAsync_Cancelled_LeavesNoNewServer()
    {
        var source = NewServer("Survival");
        Write(_store.InstanceDir(source.Id), "world/level.dat", "level");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _copier.CopyAsync(source, "Copy", cancellationToken: new CancellationToken(true)));

        Assert.Single(_store.LoadAll());
        Assert.False(Directory.Exists(_store.InstanceDir("copy")));
    }

    [Fact]
    public async Task CopyAsync_SameName_GetsItsOwnFolder()
    {
        var source = NewServer("Survival");

        var copy = await _copier.CopyAsync(source, "Survival");

        Assert.Equal("survival-2", copy.Id);
        Assert.True(File.Exists(Path.Combine(_store.InstanceDir(source.Id), InstanceMetadata.FileName)));
    }

    private InstanceMetadata NewServer(string name)
    {
        var metadata = new InstanceMetadata
        {
            Id = _store.CreateFolder(name),
            Name = name,
            Type = ServerType.Vanilla,
            MinecraftVersion = "1.21.1",
            JavaMajor = 21,
            MemoryMb = 2048,
            CreatedAt = _clock.GetUtcNow().AddDays(-1),
        };
        _store.Save(metadata);
        return metadata;
    }

    private static void Write(string dir, string relative, string content)
    {
        var path = Path.Combine(dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
