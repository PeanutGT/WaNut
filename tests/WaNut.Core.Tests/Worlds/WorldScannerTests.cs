using System.IO.Compression;
using WaNut.Core.Tests.TestSupport;
using WaNut.Core.Worlds;

namespace WaNut.Core.Tests.Worlds;

public sealed class WorldScannerTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private string AppData => Path.Combine(_temp.Path, "AppData");

    private string Profile => Path.Combine(_temp.Path, "User");

    [Fact]
    public void Find_EveryLauncher_ListsInstancesWithTheirNames()
    {
        Directory.CreateDirectory(Path.Combine(AppData, ".minecraft"));
        Directory.CreateDirectory(Path.Combine(AppData, "ModrinthApp", "profiles", "Fabulously Optimized"));
        var prism = Path.Combine(AppData, "PrismLauncher", "instances", "inst1");
        Directory.CreateDirectory(Path.Combine(prism, "minecraft"));
        File.WriteAllText(Path.Combine(prism, "instance.cfg"), "InstanceType=OneSix\nname=Create Pack\n");
        var curse = Path.Combine(Profile, "curseforge", "minecraft", "Instances", "abc");
        Directory.CreateDirectory(curse);
        File.WriteAllText(Path.Combine(curse, "minecraftinstance.json"), """{"name":"All the Mods"}""");

        var found = SaveLocations.Find(AppData, Profile);

        Assert.Equal(
            [(LauncherKind.Official, null), (LauncherKind.ModrinthApp, "Fabulously Optimized"), (LauncherKind.Prism, "Create Pack"), (LauncherKind.CurseForge, "All the Mods")],
            found.Select(f => (f.Launcher, f.InstanceName)));
        Assert.Equal(Path.Combine(prism, "minecraft", "saves"), found[2].SavesDir);
    }

    [Fact]
    public void Scan_Saves_ListsWorldsNewestFirstAndSkipsBrokenOnes()
    {
        var game = Path.Combine(AppData, ".minecraft");
        WorldFixture.Create(Path.Combine(game, "saves", "old"), "Old", lastPlayed: 1_000);
        WorldFixture.Create(Path.Combine(game, "saves", "new"), "New", lastPlayed: 2_000);
        Directory.CreateDirectory(Path.Combine(game, "saves", "empty"));
        Directory.CreateDirectory(Path.Combine(game, "saves", "broken"));
        File.WriteAllText(Path.Combine(game, "saves", "broken", "level.dat"), "not nbt");

        var worlds = WorldScanner.Scan(SaveLocations.Find(AppData, Profile));

        Assert.Equal(["New", "Old"], worlds.Select(w => w.Details.LevelName));
        Assert.All(worlds, w => Assert.Equal(LauncherKind.Official, w.Location!.Launcher));
    }

    [Fact]
    public void Scan_ModdedWorld_ListsInstanceMods()
    {
        var game = Path.Combine(AppData, "ModrinthApp", "profiles", "Pack");
        WorldFixture.Create(Path.Combine(game, "saves", "w"), brands: ["fabric"]);
        Directory.CreateDirectory(Path.Combine(game, "mods"));
        File.WriteAllText(Path.Combine(game, "mods", "sodium.jar"), "");
        File.WriteAllText(Path.Combine(game, "mods", "old.jar.disabled"), "");

        var world = Assert.Single(WorldScanner.Scan(SaveLocations.Find(AppData, Profile)));

        Assert.Equal(["sodium.jar"], world.ModFiles);
    }

    [Theory]
    [InlineData("")]
    [InlineData("My World/")]
    [InlineData("export/My World/")]
    public void FromZip_LevelDatUpToTwoFoldersDown_FindsRoot(string root)
    {
        var world = WorldFixture.Create(Path.Combine(_temp.Path, "src"), "Zipped");
        var zip = Path.Combine(_temp.Path, "world.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            archive.CreateEntryFromFile(Path.Combine(world, "level.dat"), root + "level.dat");
            archive.CreateEntry("__MACOSX/x/level.dat");
        }

        var source = WorldScanner.FromZip(zip);

        Assert.Equal(root, source.ZipRoot);
        Assert.Equal("Zipped", source.Details.LevelName);
    }

    [Fact]
    public void FromZip_NoLevelDat_ThrowsNotAWorld()
    {
        var zip = Path.Combine(_temp.Path, "mods.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            archive.CreateEntry("readme.txt");
        }

        Assert.Equal(WorldProblem.NotAWorld, Assert.Throws<WorldException>(() => WorldScanner.FromZip(zip)).Problem);
    }

    [Fact]
    public void FromZip_NotAZip_ThrowsDamagedZip()
    {
        var zip = Path.Combine(_temp.Path, "fake.zip");
        File.WriteAllText(zip, "hello");

        Assert.Equal(WorldProblem.DamagedZip, Assert.Throws<WorldException>(() => WorldScanner.FromZip(zip)).Problem);
    }

    [Fact]
    public void FromFolder_NoLevelDat_ThrowsNotAWorld()
    {
        Assert.Equal(WorldProblem.NotAWorld, Assert.Throws<WorldException>(() => WorldScanner.FromFolder(_temp.Path)).Problem);
    }
}
