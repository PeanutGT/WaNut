using WaNut.Core.Instances;
using WaNut.Core.Settings;

namespace WaNut.Core.Tests.Settings;

public class PaperTweaksTests
{
    // Shortened from what Paper 1.21 writes to config/paper-world-defaults.yml.
    private const string PaperDefaults = """
        _version: 31
        chunks:
          auto-save-interval: default
        collisions:
          allow-player-cramming-damage: false
          max-entity-collisions: 8
        environment:
          disable-ice-and-snow: false
          optimize-explosions: false
        tick-rates:
          behavior:
            villager:
              validatenearbypoi: -1
          sensor:
            villager:
              secondarypoisensor: 40
          wet-farmland: 1
        """;

    [Fact]
    public void Apply_On_SetsTheLighterValuesAndKeepsTheRest()
    {
        var on = PaperTweaks.Apply(PaperDefaults, on: true);

        Assert.True(PaperTweaks.IsOnIn(on));
        Assert.Equal("2", YamlScalars.Get(on, "collisions.max-entity-collisions"));
        Assert.Equal("80", YamlScalars.Get(on, "tick-rates.sensor.villager.secondarypoisensor"));
        Assert.Equal("1", YamlScalars.Get(on, "tick-rates.wet-farmland"));
        Assert.Equal("false", YamlScalars.Get(on, "collisions.allow-player-cramming-damage"));
        Assert.Equal(PaperDefaults.Split('\n').Length, on.Split('\n').Length);
    }

    [Fact]
    public void Apply_OffAfterOn_GivesBackPaperDefaults()
    {
        var off = PaperTweaks.Apply(PaperTweaks.Apply(PaperDefaults, on: true), on: false);

        Assert.Equal(PaperDefaults, off);
        Assert.False(PaperTweaks.IsOnIn(off));
    }

    [Fact]
    public void Apply_KeysMissing_AddsThemUnderTheirParents()
    {
        const string sparse = """
            _version: 31
            collisions:
              allow-player-cramming-damage: false
            misc:
              redstone-implementation: VANILLA
            """;

        var on = PaperTweaks.Apply(sparse, on: true);

        Assert.True(PaperTweaks.IsOnIn(on));
        Assert.Contains("collisions:\n  allow-player-cramming-damage: false\n  max-entity-collisions: 2\nmisc:", on.Replace("\r\n", "\n"));
        Assert.Equal("VANILLA", YamlScalars.Get(on, "misc.redstone-implementation"));
    }

    [Fact]
    public void Get_SameKeyUnderAnotherParent_IsNotMistakenForIt()
    {
        const string yaml = """
            a:
              villager: 1
            b:
              villager: 2
            """;

        Assert.Equal("2", YamlScalars.Get(yaml, "b.villager"));
        Assert.Null(YamlScalars.Get(yaml, "c.villager"));
    }

    [Fact]
    public void Set_KeepsATrailingComment()
    {
        var text = YamlScalars.Set("a:\n  b: 1 # note\n", "a.b", "5");

        Assert.Equal("a:\n  b: 5 # note\n", text);
    }

    [Theory]
    [InlineData(ServerType.Paper, "1.21.4", true)]
    [InlineData(ServerType.Paper, "1.19", true)]
    [InlineData(ServerType.Paper, "1.18.2", false)]
    [InlineData(ServerType.Fabric, "1.21.4", false)]
    public void Supports_OnlyPaperWithWorldDefaultsFile(ServerType type, string version, bool expected) =>
        Assert.Equal(expected, PaperTweaks.Supports(type, version));
}
