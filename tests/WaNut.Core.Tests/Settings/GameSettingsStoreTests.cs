using WaNut.Core.Instances;
using WaNut.Core.Settings;
using WaNut.Core.Tests.TestSupport;

namespace WaNut.Core.Tests.Settings;

public sealed class GameSettingsStoreTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private static InstanceMetadata Metadata(string version) => new()
    {
        Id = "test",
        Name = "Test",
        Type = ServerType.Vanilla,
        MinecraftVersion = version,
        JavaMajor = 21,
        MemoryMb = 2048,
        CreatedAt = DateTimeOffset.UnixEpoch,
    };

    private string PropertiesPath => Path.Combine(_dir.Path, GameSettingsStore.PropertiesFileName);

    private void WriteProperties(string text) => File.WriteAllText(PropertiesPath, text);

    [Fact]
    public void Load_NoFile_GivesMinecraftDefaults()
    {
        Assert.Equal(new GameSettings(), GameSettingsStore.Load(_dir.Path, Metadata("1.21.8")));
    }

    [Theory]
    [InlineData("1.21.8", false)]
    [InlineData("1.21.9", true)]
    [InlineData("26.3", true)]
    public void Load_CommandBlocksDefault_FollowsVersion(string version, bool expected)
    {
        Assert.Equal(expected, GameSettingsStore.Load(_dir.Path, Metadata(version)).CommandBlocks);
    }

    [Fact]
    public void Load_ReadsProperties()
    {
        WriteProperties("gamemode=creative\ndifficulty=hard\npvp=false\nmax-players=8\nmotd=Hi\nenable-command-block=true\nspawn-monsters=false\nlevel-seed=123\n");

        var settings = GameSettingsStore.Load(_dir.Path, Metadata("1.21.1"));

        Assert.Equal(GameMode.Creative, settings.GameMode);
        Assert.Equal(Difficulty.Hard, settings.Difficulty);
        Assert.False(settings.Pvp);
        Assert.Equal(8, settings.MaxPlayers);
        Assert.Equal("Hi", settings.Motd);
        Assert.True(settings.CommandBlocks);
        Assert.False(settings.SpawnMonsters);
        Assert.Equal("123", settings.Seed);
    }

    [Theory]
    [InlineData("1", GameMode.Creative)]
    [InlineData("ADVENTURE", GameMode.Adventure)]
    [InlineData("nonsense", GameMode.Survival)]
    [InlineData("9", GameMode.Survival)]
    public void Load_GameModeAcceptsNamesAndNumbers(string value, GameMode expected)
    {
        WriteProperties($"gamemode={value}\n");

        Assert.Equal(expected, GameSettingsStore.Load(_dir.Path, Metadata("1.21.1")).GameMode);
    }

    [Fact]
    public void Load_Version26_ReadsRulesFromMetadataNotProperties()
    {
        WriteProperties("pvp=false\n");
        var metadata = Metadata("26.3") with { GameRules = new Dictionary<string, bool> { ["SpawnMonsters"] = false } };

        var settings = GameSettingsStore.Load(_dir.Path, metadata);

        Assert.True(settings.Pvp);
        Assert.False(settings.SpawnMonsters);
    }

    [Fact]
    public void Save_WritesOnlyChangedKeys()
    {
        const string original = "#comment\r\ngamemode=weird\r\ndifficulty=easy\r\nunknown=keep\r\n";
        WriteProperties(original);
        var metadata = Metadata("1.21.1");
        var loaded = GameSettingsStore.Load(_dir.Path, metadata);

        GameSettingsStore.Save(_dir.Path, metadata, loaded, loaded with { Difficulty = Difficulty.Hard });

        // "weird" could not be read, but the user did not touch the game mode, so it stays.
        Assert.Equal("#comment\r\ngamemode=weird\r\ndifficulty=hard\r\nunknown=keep\r\n", File.ReadAllText(PropertiesPath));
    }

    [Fact]
    public void Save_NothingChanged_DoesNotCreateTheFile()
    {
        var metadata = Metadata("1.21.1");
        var loaded = GameSettingsStore.Load(_dir.Path, metadata);

        GameSettingsStore.Save(_dir.Path, metadata, loaded, loaded);

        Assert.False(File.Exists(PropertiesPath));
    }

    [Fact]
    public void Save_OldVersion_PvpGoesToProperties_KeepInventoryToRules()
    {
        var metadata = Metadata("1.21.1");
        var loaded = GameSettingsStore.Load(_dir.Path, metadata);

        var saved = GameSettingsStore.Save(_dir.Path, metadata, loaded, loaded with { Pvp = false, KeepInventory = true });

        Assert.Contains("pvp=false", File.ReadAllText(PropertiesPath), StringComparison.Ordinal);
        Assert.Equal(new Dictionary<string, bool> { ["KeepInventory"] = true }, saved.GameRules);
        Assert.Equal(["gamerule keepInventory true"], GameSettingsStore.RuleCommands(saved));
    }

    [Fact]
    public void Save_Version26_PvpBecomesAGameRule()
    {
        var metadata = Metadata("26.3");
        var loaded = GameSettingsStore.Load(_dir.Path, metadata);

        var saved = GameSettingsStore.Save(_dir.Path, metadata, loaded, loaded with { Pvp = false, KeepInventory = true });

        Assert.False(File.Exists(PropertiesPath));
        Assert.Equal(["gamerule keep_inventory true", "gamerule pvp false"], GameSettingsStore.RuleCommands(saved));
    }

    [Fact]
    public void ChangedRuleCommands_OnlyChangedRules()
    {
        var before = new GameSettings();

        var commands = GameSettingsStore.ChangedRuleCommands(Metadata("26.3"), before, before with { SpawnMonsters = false, Difficulty = Difficulty.Hard });

        Assert.Equal(["gamerule spawn_monsters false"], commands);
    }

    [Fact]
    public void ChangedRuleCommands_OldVersion_PropertySettingsSendNothing()
    {
        var before = new GameSettings();

        Assert.Empty(GameSettingsStore.ChangedRuleCommands(Metadata("1.21.1"), before, before with { Pvp = false }));
    }

    [Fact]
    public void WriteDefaults_SetsMotdAndTurnsWhitelistOff()
    {
        GameSettingsStore.WriteDefaults(_dir.Path, "Peanut's world 花生");

        var settings = GameSettingsStore.Load(_dir.Path, Metadata("26.3"));
        Assert.Equal("Peanut's world 花生", settings.Motd);
        Assert.Contains("white-list=false", File.ReadAllText(PropertiesPath), StringComparison.Ordinal);
        Assert.DoesNotContain("management-server", File.ReadAllText(PropertiesPath), StringComparison.Ordinal);
    }

    [Fact]
    public void WorldExists_FollowsLevelName()
    {
        WriteProperties("level-name=survival\n");
        Assert.False(GameSettingsStore.WorldExists(_dir.Path));

        Directory.CreateDirectory(Path.Combine(_dir.Path, "world"));
        Assert.False(GameSettingsStore.WorldExists(_dir.Path));

        Directory.CreateDirectory(Path.Combine(_dir.Path, "survival"));
        Assert.True(GameSettingsStore.WorldExists(_dir.Path));
    }
}
