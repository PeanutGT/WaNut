using WaNut.Core.Instances;
using WaNut.Core.Worlds;

namespace WaNut.Core.Tests.Worlds;

public sealed class WorldSourceTests
{
    private static readonly SaveLocation Instance = new(LauncherKind.ModrinthApp, "Fabric pack", Path.Combine("C:", "pack"));

    private static WorldSource World(string[] loaders, SaveLocation? location, params string[] mods) =>
        new("world", null, new WorldDetails("World", "1.21.1", null, loaders, HasPlayer: false), location, mods);

    [Fact]
    public void CanInstallModsOn_FabricWorldWithModsToFabric_IsTrue()
    {
        var world = World(["fabric"], Instance, "sodium.jar");

        Assert.True(world.CanInstallModsOn(ServerType.Fabric));
        Assert.Equal([Path.Combine("C:", "pack", "mods", "sodium.jar")], world.ModPaths);
    }

    [Theory]
    [InlineData(ServerType.Paper)]
    [InlineData(ServerType.Vanilla)]
    [InlineData(null)]
    public void CanInstallModsOn_OtherServerTypes_IsFalse(ServerType? type)
    {
        Assert.False(World(["fabric"], Instance, "sodium.jar").CanInstallModsOn(type));
    }

    [Fact]
    public void CanInstallModsOn_ForgeWorld_IsFalse()
    {
        Assert.False(World(["forge"], Instance, "create.jar").CanInstallModsOn(ServerType.Fabric));
    }

    [Fact]
    public void CanInstallModsOn_FolderPickedByHand_IsFalse()
    {
        // A world picked from a folder has no launcher instance, so there is no mods folder to read.
        Assert.False(World(["fabric"], null).CanInstallModsOn(ServerType.Fabric));
    }
}
