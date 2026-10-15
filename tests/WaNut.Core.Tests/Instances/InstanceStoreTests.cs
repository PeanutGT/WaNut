using WaNut.Core.Instances;
using WaNut.Core.Tests.TestSupport;

namespace WaNut.Core.Tests.Instances;

public sealed class InstanceStoreTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly InstanceStore _store;

    public InstanceStoreTests()
    {
        _store = new InstanceStore(new LauncherPaths(_temp.Path));
    }

    public void Dispose() => _temp.Dispose();

    [Theory]
    [InlineData("My Server", "my-server")]
    [InlineData("  Survival 1.21.1!! ", "survival-1.21.1")]
    [InlineData("我的伺服器", "server")]
    [InlineData("a/b\\c:d", "a-b-c-d")]
    [InlineData("---", "server")]
    public void ToFolderName_Name_ReturnsSafeFolderName(string name, string expected)
    {
        Assert.Equal(expected, InstanceStore.ToFolderName(name));
    }

    [Fact]
    public void ToFolderName_VeryLongName_IsShortened()
    {
        Assert.True(InstanceStore.ToFolderName(new string('a', 200)).Length <= 48);
    }

    [Fact]
    public void CreateFolder_SameNameTwice_ReturnsUniqueIds()
    {
        var first = _store.CreateFolder("My Server");
        var second = _store.CreateFolder("My Server");

        Assert.Equal("my-server", first);
        Assert.Equal("my-server-2", second);
        Assert.True(Directory.Exists(_store.InstanceDir(second)));
    }

    [Fact]
    public void SaveThenLoadAll_OneInstance_RoundTrips()
    {
        var id = _store.CreateFolder("Test");
        var saved = Sample(id, "Test", new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero));

        _store.Save(saved);
        var loaded = Assert.Single(_store.LoadAll());

        Assert.Equivalent(saved, loaded, strict: true);
    }

    [Fact]
    public void SaveThenLoadAll_KeepsGameRulesAndPendingCommands()
    {
        var saved = Sample("test", "Test", DateTimeOffset.UnixEpoch) with
        {
            GameRules = new Dictionary<string, bool> { ["keepInventory"] = true },
            PendingCommands = ["whitelist add Notch"],
        };

        _store.Save(saved);
        var loaded = Assert.Single(_store.LoadAll());

        Assert.Equal(saved.GameRules, loaded.GameRules);
        Assert.Equal(saved.PendingCommands, loaded.PendingCommands);
    }

    [Fact]
    public void LoadAll_SeveralInstances_ReturnsOldestFirst()
    {
        _store.Save(Sample(_store.CreateFolder("New"), "New", new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero)));
        _store.Save(Sample(_store.CreateFolder("Old"), "Old", new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero)));

        Assert.Equal(["Old", "New"], _store.LoadAll().Select(i => i.Name));
    }

    [Fact]
    public void LoadAll_DamagedOrMissingFile_SkipsItAndKeepsFolder()
    {
        _store.Save(Sample(_store.CreateFolder("Good"), "Good", DateTimeOffset.UnixEpoch));
        var broken = _store.CreateFolder("Broken");
        File.WriteAllText(Path.Combine(_store.InstanceDir(broken), InstanceMetadata.FileName), "{ not json");
        _store.CreateFolder("Empty");

        var all = _store.LoadAll();

        Assert.Equal("Good", Assert.Single(all).Name);
        Assert.True(Directory.Exists(_store.InstanceDir(broken)));
    }

    [Fact]
    public void LoadAll_NoInstancesFolder_ReturnsEmpty()
    {
        Assert.Empty(new InstanceStore(new LauncherPaths(Path.Combine(_temp.Path, "missing"))).LoadAll());
    }

    private static InstanceMetadata Sample(string id, string name, DateTimeOffset createdAt) => new()
    {
        Id = id,
        Name = name,
        Type = ServerType.Vanilla,
        MinecraftVersion = "1.21.1",
        JavaMajor = 21,
        MemoryMb = 4096,
        CreatedAt = createdAt,
    };
}
