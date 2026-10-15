using System.Globalization;

namespace WaNut.Core.Settings;

/// <summary>A game setting that is, or on some versions becomes, a game rule.</summary>
public enum RuleSetting
{
    KeepInventory,
    Pvp,
    CommandBlocks,
    SpawnMonsters,
}

/// <summary>
/// What a Minecraft version supports and what it calls things. Checked against real servers on 2026-10-15:
/// up to 1.21.8, PvP, command blocks and monster spawning are server.properties keys; from 1.21.9 they are game
/// rules (camelCase: commandBlocksEnabled, spawnMonsters); from 1.21.11 every game rule is snake_case
/// (keep_inventory, command_blocks_work, spawn_monsters).
/// </summary>
public sealed class GameFeatures
{
    // First versions with each change.
    private static readonly Version SimulationDistanceSince = new(1, 18, 0);
    private static readonly Version SnakeCaseRulesSince = new(1, 21, 11);
    private static readonly Version PropertiesAsRulesSince = new(1, 21, 9);

    private readonly bool _snakeCase;

    private GameFeatures(Version version)
    {
        HasSimulationDistance = version >= SimulationDistanceSince;
        SettingsAreGameRules = version >= PropertiesAsRulesSince;
        _snakeCase = version >= SnakeCaseRulesSince;
    }

    public bool HasSimulationDistance { get; }

    /// <summary>True when PvP, command blocks and monster spawning are game rules instead of server.properties keys.</summary>
    public bool SettingsAreGameRules { get; }

    public static GameFeatures For(string minecraftVersion) => new(ParseVersion(minecraftVersion));

    /// <summary>True when <paramref name="setting"/> is a game rule on this version rather than a server.properties key.</summary>
    public bool IsGameRule(RuleSetting setting) => setting == RuleSetting.KeepInventory || SettingsAreGameRules;

    /// <summary>The game rule's name on this version, or null when the setting is a server.properties key here.</summary>
    public string? RuleName(RuleSetting setting)
    {
        if (!IsGameRule(setting))
        {
            return null;
        }

        return (setting, _snakeCase) switch
        {
            (RuleSetting.KeepInventory, true) => "keep_inventory",
            (RuleSetting.KeepInventory, false) => "keepInventory",
            (RuleSetting.Pvp, _) => "pvp",
            (RuleSetting.CommandBlocks, true) => "command_blocks_work",
            (RuleSetting.CommandBlocks, false) => "commandBlocksEnabled",
            (RuleSetting.SpawnMonsters, true) => "spawn_monsters",
            (RuleSetting.SpawnMonsters, false) => "spawnMonsters",
            _ => throw new ArgumentOutOfRangeException(nameof(setting)),
        };
    }

    /// <summary>
    /// The value a setting has when nobody changed it. Command blocks are off as a server.properties key
    /// (up to 1.21.8) but on as a game rule (from 1.21.9).
    /// </summary>
    public bool DefaultValue(RuleSetting setting) => setting switch
    {
        RuleSetting.KeepInventory => false,
        RuleSetting.CommandBlocks => IsGameRule(setting),
        _ => true,
    };

    /// <summary>"1.21.1" → 1.21.1, "26.3" → 26.3.0. Anything after the numbers (such as "-rc1") is ignored.</summary>
    public static Version ParseVersion(string id)
    {
        var numbers = id.Split('.', '-', ' ')
            .TakeWhile(part => part.Length > 0 && part.All(char.IsAsciiDigit))
            .Select(part => int.Parse(part, CultureInfo.InvariantCulture))
            .Concat([0, 0, 0])
            .Take(3)
            .ToArray();
        return new Version(numbers[0], numbers[1], numbers[2]);
    }
}
