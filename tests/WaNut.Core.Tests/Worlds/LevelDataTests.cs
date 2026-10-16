using WaNut.Core.Tests.TestSupport;
using WaNut.Core.Worlds;

namespace WaNut.Core.Tests.Worlds;

public sealed class LevelDataTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void ReadDetails_VanillaWorld_ReadsNameVersionAndPlayer()
    {
        var dir = WorldFixture.Create(Path.Combine(_temp.Path, "w"), "Island", "1.20.4", WorldFixture.Player("sword"), ["vanilla"]);

        var details = LevelData.ReadDetails(Path.Combine(dir, "level.dat"), "fallback");

        Assert.Equal("Island", details.LevelName);
        Assert.Equal("1.20.4", details.VersionName);
        Assert.True(details.HasPlayer);
        Assert.False(details.IsModded);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000), details.LastPlayed);
    }

    [Fact]
    public void ReadDetails_FabricWorld_ListsLoader()
    {
        var dir = WorldFixture.Create(Path.Combine(_temp.Path, "w"), brands: ["vanilla", "Fabric"]);

        var details = LevelData.ReadDetails(Path.Combine(dir, "level.dat"), "fallback");

        Assert.True(details.IsModded);
        Assert.Equal(["fabric"], details.ModLoaders);
    }

    [Fact]
    public void ReadDetails_OldWorldWithoutVersion_UsesFallbackNameAndNullVersion()
    {
        var dir = WorldFixture.Create(Path.Combine(_temp.Path, "w"), name: "", version: null);

        var details = LevelData.ReadDetails(Path.Combine(dir, "level.dat"), "folder");

        Assert.Equal("folder", details.LevelName);
        Assert.Null(details.VersionName);
        Assert.False(details.HasPlayer);
    }

    [Fact]
    public void WritePlayer_ReplacesHostAndKeepsOtherData()
    {
        var dir = WorldFixture.Create(Path.Combine(_temp.Path, "w"), "Keep", player: WorldFixture.Player("old"));
        var levelDat = Path.Combine(dir, "level.dat");

        LevelData.WritePlayer(levelDat, WorldFixture.Player("new"));

        Assert.Equal("new", LevelData.ReadPlayer(levelDat)!.String("SelectedItem"));
        Assert.Equal("Keep", LevelData.ReadDetails(levelDat, "x").LevelName);
    }
}
