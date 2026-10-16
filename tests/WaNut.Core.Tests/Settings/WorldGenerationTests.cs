using System.Text.Json.Nodes;
using WaNut.Core.Settings;

namespace WaNut.Core.Tests.Settings;

public sealed class WorldGenerationTests
{
    private static Dictionary<string, string> Properties(WorldGeneration world, string version) =>
        world.Properties(version).ToDictionary(p => p.Key, p => p.Value);

    private static JsonObject FlatJson(WorldGeneration world, string version) =>
        JsonNode.Parse(Properties(world, version)["generator-settings"])!.AsObject();

    [Fact]
    public void Properties_Defaults_WritesNothing()
    {
        Assert.Empty(new WorldGeneration().Properties("1.21.1"));
    }

    [Fact]
    public void Properties_Seed_IsTrimmed()
    {
        Assert.Equal("-123", Properties(new WorldGeneration("  -123 "), "1.21.1")["level-seed"]);
    }

    [Theory]
    [InlineData(WorldType.Superflat, "1.18.2", "flat")]
    [InlineData(WorldType.LargeBiomes, "1.16.5", "largebiomes")]
    [InlineData(WorldType.Amplified, "1.18.2", "amplified")]
    [InlineData(WorldType.Superflat, "1.19", "minecraft:flat")]
    [InlineData(WorldType.LargeBiomes, "1.21.1", "minecraft:large_biomes")]
    [InlineData(WorldType.Amplified, "26.3", "minecraft:amplified")]
    public void Properties_Type_UsesTheVersionsName(WorldType type, string version, string expected)
    {
        Assert.Equal(expected, Properties(new WorldGeneration(Type: type), version)["level-type"]);
    }

    [Fact]
    public void Properties_ClassicFlat_LeavesMinecraftsDefaultLayers()
    {
        Assert.DoesNotContain("generator-settings", Properties(new WorldGeneration(Type: WorldType.Superflat), "1.21.1").Keys);
    }

    [Fact]
    public void Properties_PresetOnNormalWorld_IsIgnored()
    {
        Assert.Empty(new WorldGeneration(Preset: FlatPreset.Desert).Properties("1.21.1"));
    }

    [Fact]
    public void Properties_DesertOnNewVersion_UsesStructureSets()
    {
        var json = FlatJson(new WorldGeneration(Type: WorldType.Superflat, Preset: FlatPreset.Desert), "1.21.1");

        Assert.Equal("minecraft:desert", json["biome"]!.GetValue<string>());
        Assert.Equal(64, json["layers"]!.AsArray().Sum(l => l!["height"]!.GetValue<int>()));
        Assert.Contains("minecraft:villages", json["structure_overrides"]!.AsArray().Select(s => s!.GetValue<string>()));
        Assert.False(json.ContainsKey("structures"));
    }

    [Fact]
    public void Properties_DesertOnOldVersion_UsesStructurePlacements()
    {
        var json = FlatJson(new WorldGeneration(Type: WorldType.Superflat, Preset: FlatPreset.Desert), "1.16.5");

        var structures = json["structures"]!["structures"]!.AsObject();
        Assert.Equal(32, structures["minecraft:village"]!["spacing"]!.GetValue<int>());
        Assert.False(json.ContainsKey("structure_overrides"));
    }

    [Theory]
    [InlineData("1.16.5")]
    [InlineData("26.3")]
    public void Properties_Void_IsOneAirLayerWithFeatures(string version)
    {
        var json = FlatJson(new WorldGeneration(Type: WorldType.Superflat, Preset: FlatPreset.Void), version);

        Assert.Equal("minecraft:the_void", json["biome"]!.GetValue<string>());
        Assert.Equal("minecraft:air", Assert.Single(json["layers"]!.AsArray())!["block"]!.GetValue<string>());
        Assert.True(json["features"]!.GetValue<bool>());
    }

    [Fact]
    public void Properties_CustomSettings_ReplacePresetOnOneLine()
    {
        var world = new WorldGeneration(Type: WorldType.Superflat, Preset: FlatPreset.Desert, CustomSettings: "{\n  \"biome\": \"minecraft:plains\"\n}");

        Assert.Equal("{\"biome\":\"minecraft:plains\"}", Properties(world, "1.21.1")["generator-settings"]);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("  ", true)]
    [InlineData("{\"layers\":[]}", true)]
    [InlineData("[1]", false)]
    [InlineData("{oops", false)]
    public void IsValidCustomSettings_AcceptsOnlyObjects(string json, bool expected)
    {
        Assert.Equal(expected, WorldGeneration.IsValidCustomSettings(json));
    }
}
