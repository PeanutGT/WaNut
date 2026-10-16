using WaNut.Core.Settings;

namespace WaNut.Core.Tests.Settings;

public sealed class GameFeaturesTests
{
    [Theory]
    [InlineData("1.21.1", 1, 21, 1)]
    [InlineData("26.3", 26, 3, 0)]
    [InlineData("1.16.5", 1, 16, 5)]
    [InlineData("1.21-pre1", 1, 21, 0)]
    public void ParseVersion_ReadsTheNumbers(string id, int major, int minor, int build)
    {
        Assert.Equal(new Version(major, minor, build), GameFeatures.ParseVersion(id));
    }

    [Theory]
    [InlineData("1.17.1", false)]
    [InlineData("1.18", true)]
    [InlineData("26.3", true)]
    public void HasSimulationDistance_From118(string version, bool expected)
    {
        Assert.Equal(expected, GameFeatures.For(version).HasSimulationDistance);
    }

    [Theory]
    [InlineData("1.18.1", false, false)]
    [InlineData("1.18.2", true, false)]
    [InlineData("1.19", true, true)]
    [InlineData("26.3", true, true)]
    public void WorldGeneration_FollowsVersion(string version, bool structureSets, bool worldPresets)
    {
        var features = GameFeatures.For(version);
        Assert.Equal(structureSets, features.HasStructureSets);
        Assert.Equal(worldPresets, features.HasWorldPresets);
    }

    [Theory]
    [InlineData("1.21.1")]
    [InlineData("1.21.8")]
    public void OldVersion_UsesPropertiesAndCamelCaseRules(string version)
    {
        var features = GameFeatures.For(version);

        Assert.Equal("keepInventory", features.RuleName(RuleSetting.KeepInventory));
        Assert.Null(features.RuleName(RuleSetting.Pvp));
        Assert.Null(features.RuleName(RuleSetting.CommandBlocks));
        Assert.Null(features.RuleName(RuleSetting.SpawnMonsters));
    }

    [Theory]
    [InlineData("1.21.9")]
    [InlineData("1.21.10")]
    public void From1219_PvpIsACamelCaseRule(string version)
    {
        var features = GameFeatures.For(version);

        Assert.Equal("keepInventory", features.RuleName(RuleSetting.KeepInventory));
        Assert.Equal("pvp", features.RuleName(RuleSetting.Pvp));
        Assert.Equal("commandBlocksEnabled", features.RuleName(RuleSetting.CommandBlocks));
        Assert.Equal("spawnMonsters", features.RuleName(RuleSetting.SpawnMonsters));
    }

    [Theory]
    [InlineData("1.21.11")]
    [InlineData("26.3")]
    public void From12111_UsesSnakeCaseRulesForEverything(string version)
    {
        var features = GameFeatures.For(version);

        Assert.Equal("keep_inventory", features.RuleName(RuleSetting.KeepInventory));
        Assert.Equal("pvp", features.RuleName(RuleSetting.Pvp));
        Assert.Equal("command_blocks_work", features.RuleName(RuleSetting.CommandBlocks));
        Assert.Equal("spawn_monsters", features.RuleName(RuleSetting.SpawnMonsters));
    }
}
