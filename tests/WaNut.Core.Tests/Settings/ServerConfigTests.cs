using WaNut.Core.Properties;
using WaNut.Core.Settings.Config;

namespace WaNut.Core.Tests.Settings;

public sealed class ServerConfigTests
{
    private const string Modded =
        "#Minecraft server properties\r\n" +
        "#Tue Oct 14 23:56:00 CST 2026\r\n" +
        "gamemode=creative\r\n" +
        "# kept comment\r\n" +
        "some-mod-setting=keep me\r\n" +
        "pvp=false\r\n" +
        "view-distance=12\r\n";

    private static ServerConfig Load(string text = Modded, string version = "1.21.1") =>
        ServerConfig.Load(PropertiesFile.Parse(text), version);

    private static Core.Instances.InstanceMetadata Metadata => new()
    {
        Id = "test",
        Name = "Test",
        Type = Core.Instances.ServerType.Vanilla,
        MinecraftVersion = "1.21.11",
        JavaMajor = 21,
        MemoryMb = 2048,
        CreatedAt = DateTimeOffset.UnixEpoch,
    };

    [Fact]
    public void Load_UnknownKey_BecomesRawOnlyItemAfterCatalog()
    {
        var config = Load();

        var mod = config.Items[^1];
        Assert.Equal("some-mod-setting", mod.Meta.Key);
        Assert.Equal(ConfigSection.RawOnly, mod.Meta.Section);
        Assert.Equal("keep me", mod.RawValue);
    }

    [Fact]
    public void Load_CatalogKeyMissingFromFile_ShowsDefault()
    {
        Assert.Equal("easy", Load().Find("difficulty")!.RawValue);
    }

    [Fact]
    public void ApplyTo_ChangedValue_KeepsCommentsOrderAndUnknownKeys()
    {
        var config = Load();
        config.Find("gamemode")!.RawValue = "survival";
        var file = PropertiesFile.Parse(Modded);

        Assert.True(config.ApplyTo(file));

        Assert.Equal(Modded.Replace("gamemode=creative", "gamemode=survival", StringComparison.Ordinal), file.ToText());
    }

    [Fact]
    public void ApplyTo_NothingChanged_WritesNothing()
    {
        var file = PropertiesFile.Parse(Modded);

        Assert.False(Load().ApplyTo(file));
        Assert.Equal(Modded, file.ToText());
    }

    [Fact]
    public void ApplyTo_MissingKeySetToDefault_IsNotAdded()
    {
        var config = Load();
        var item = config.Find("allow-flight")!;
        item.BoolValue = true;
        item.ResetToDefault();
        var file = PropertiesFile.Parse(Modded);

        Assert.False(config.ApplyTo(file));
        Assert.Null(file.Get("allow-flight"));
    }

    [Fact]
    public void ApplyTo_MissingKeyChanged_IsAppended()
    {
        var config = Load();
        config.Find("allow-flight")!.BoolValue = true;
        var file = PropertiesFile.Parse(Modded);

        config.ApplyTo(file);

        Assert.Equal("true", file.Get("allow-flight"));
        Assert.EndsWith("allow-flight=true\r\n", file.ToText(), StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyTo_KeyEditedMeanwhileOnDisk_IsNotOverwritten()
    {
        var config = Load();
        config.Find("gamemode")!.RawValue = "adventure";
        var onDisk = PropertiesFile.Parse(Modded.Replace("view-distance=12", "view-distance=20", StringComparison.Ordinal));

        config.ApplyTo(onDisk);

        Assert.Equal("20", onDisk.Get("view-distance"));
        Assert.Equal("adventure", onDisk.Get("gamemode"));
    }

    [Fact]
    public void ApplyTo_SecondCall_WritesNothingNew()
    {
        var config = Load();
        config.Find("motd")!.RawValue = "Hi";

        Assert.True(config.ApplyTo(PropertiesFile.Parse(Modded)));
        Assert.False(config.ApplyTo(PropertiesFile.Parse(Modded)));
    }

    [Fact]
    public void Load_GameRuleKeyOnNewVersion_IsKeptAsRawOnly()
    {
        var config = Load("pvp=false\n", version: "1.21.9");

        var pvp = config.FileItems.Single(i => i.Meta.Key == "pvp");
        Assert.Equal(ConfigSection.RawOnly, pvp.Meta.Section);
        Assert.Equal("false", pvp.RawValue);
    }

    [Fact]
    public void Load_GameRuleKeyOnOldVersion_IsInCore()
    {
        Assert.Equal(ConfigSection.Core, Load().Find("pvp")!.Meta.Section);
    }

    [Fact]
    public void Load_KeepInventory_IsAlwaysAGameRuleItemWithItsOwnTexts()
    {
        var item = Load().Find("keepInventory")!;

        Assert.True(item.Meta.IsGameRuleItem);
        Assert.Equal("ServerConfig_KeepInventory_Title", item.Meta.TitleKey);
        Assert.DoesNotContain(Load().FileItems, i => i.Meta.Key == "keepInventory");
    }

    [Fact]
    public void Load_PvpOnNewVersion_IsAGameRuleItemKeepingThePropertyTexts()
    {
        var pvp = Load("", version: "1.21.9").Items.Single(i => i.Meta.IsGameRuleItem && i.Meta.Key == "pvp");

        Assert.Equal(ConfigSection.Core, pvp.Meta.Section);
        Assert.Equal("ServerConfig_Pvp_Title", pvp.Meta.TitleKey);
    }

    [Fact]
    public void Load_CommandBlocksRule_DefaultsToTrueOnNewVersionAndReadsSavedValue()
    {
        var rules = new Dictionary<string, bool> { ["commandBlocks"] = false };
        var config = ServerConfig.Load(PropertiesFile.Parse(""), "1.21.9", rules);

        Assert.False(config.Find("commandBlocks")!.BoolValue);
        Assert.True(ServerConfig.Load(PropertiesFile.Parse(""), "1.21.9").Find("commandBlocks")!.BoolValue);
    }

    [Fact]
    public void ApplyRulesTo_ChangedRule_IsSavedAndSendsTheVersionsRuleName()
    {
        var config = Load(version: "1.21.11");
        config.Find("keepInventory")!.BoolValue = true;
        var metadata = Metadata;

        var saved = config.ApplyRulesTo(metadata, out var commands);

        Assert.True(saved.GameRules["keepInventory"]);
        Assert.Equal(["gamerule keep_inventory true"], commands);

        config.ApplyRulesTo(saved, out var again);
        Assert.Empty(again);
    }

    [Fact]
    public void ApplyRulesTo_NothingChanged_ReturnsSameMetadata()
    {
        var metadata = Metadata;

        Assert.Same(metadata, Load().ApplyRulesTo(metadata, out var commands));
        Assert.Empty(commands);
    }

    [Fact]
    public void ApplyTo_GameRuleItem_NeverWritesIntoTheFile()
    {
        var config = Load(version: "1.21.9");
        config.Find("keepInventory")!.BoolValue = true;
        var file = PropertiesFile.Parse(Modded);

        Assert.False(config.ApplyTo(file));
        Assert.Null(file.Get("keepInventory"));
    }

    [Fact]
    public void Load_SimulationDistanceBefore118_IsNotListed()
    {
        Assert.Null(Load(version: "1.16.5").Find("simulation-distance"));
        Assert.NotNull(Load(version: "1.18").Find("simulation-distance"));
    }

    [Fact]
    public void Catalog_KeysAreUniqueAndHaveValidRanges()
    {
        Assert.Equal(ServerConfigCatalog.All.Count, ServerConfigCatalog.All.Select(m => m.Key).Distinct().Count());
        Assert.All(ServerConfigCatalog.All, m => Assert.True(m.Min <= m.Max, m.Key));
        Assert.All(ServerConfigCatalog.All.Where(m => m.ControlType == ConfigControlType.Dropdown), m => Assert.NotEmpty(m.Options!));
    }

    [Theory]
    [InlineData("allow-flight", "ServerConfig_AllowFlight_Title")]
    [InlineData("rcon.port", "ServerConfig_RconPort_Title")]
    public void TitleKey_FromKey_IsPascalCased(string key, string expected)
    {
        Assert.Equal(expected, ConfigMetadata.KeyFor(key, "Title"));
    }
}
