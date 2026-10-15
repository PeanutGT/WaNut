using System.Globalization;
using WaNut.Core.Instances;
using WaNut.Core.Properties;

namespace WaNut.Core.Settings;

/// <summary>
/// Loads and saves <see cref="GameSettings"/>. Values that are server.properties keys go to that file; values that
/// are game rules go to instance.json and become "gamerule" commands.
/// </summary>
public static class GameSettingsStore
{
    public const string PropertiesFileName = "server.properties";

    /// <summary>Folders Minecraft creates with the world. While one exists, the seed no longer matters.</summary>
    public static bool WorldExists(string instanceDir) => Directory.Exists(Path.Combine(instanceDir, LevelName(instanceDir)));

    public static GameSettings Load(string instanceDir, InstanceMetadata metadata)
    {
        var properties = PropertiesFile.Load(Path.Combine(instanceDir, PropertiesFileName));
        var features = GameFeatures.For(metadata.MinecraftVersion);
        var defaults = new GameSettings();

        bool Flag(RuleSetting setting, string key) => features.IsGameRule(setting)
            ? metadata.GameRules.TryGetValue(setting.ToString(), out var value) ? value : features.DefaultValue(setting)
            : ParseBool(properties.Get(key)) ?? features.DefaultValue(setting);

        return new GameSettings
        {
            GameMode = ParseEnum<GameMode>(properties.Get("gamemode")) ?? defaults.GameMode,
            Difficulty = ParseEnum<Difficulty>(properties.Get("difficulty")) ?? defaults.Difficulty,
            Pvp = Flag(RuleSetting.Pvp, "pvp"),
            MaxPlayers = ParseInt(properties.Get("max-players")) ?? defaults.MaxPlayers,
            Motd = properties.Get("motd") ?? defaults.Motd,
            KeepInventory = Flag(RuleSetting.KeepInventory, string.Empty),
            ViewDistance = ParseInt(properties.Get("view-distance")) ?? defaults.ViewDistance,
            SimulationDistance = ParseInt(properties.Get("simulation-distance")) ?? defaults.SimulationDistance,
            SpawnProtection = ParseInt(properties.Get("spawn-protection")) ?? defaults.SpawnProtection,
            CommandBlocks = Flag(RuleSetting.CommandBlocks, "enable-command-block"),
            AllowFlight = ParseBool(properties.Get("allow-flight")) ?? defaults.AllowFlight,
            SpawnMonsters = Flag(RuleSetting.SpawnMonsters, "spawn-monsters"),
            Hardcore = ParseBool(properties.Get("hardcore")) ?? defaults.Hardcore,
            Seed = properties.Get("level-seed") ?? defaults.Seed,
        };
    }

    /// <summary>
    /// Writes what changed between <paramref name="loaded"/> and <paramref name="edited"/>. Only changed keys are
    /// touched, so a value WaNut could not read is never replaced by a default. Returns the metadata with the
    /// changed game rules; the caller saves it.
    /// </summary>
    public static InstanceMetadata Save(string instanceDir, InstanceMetadata metadata, GameSettings loaded, GameSettings edited)
    {
        var features = GameFeatures.For(metadata.MinecraftVersion);
        var path = Path.Combine(instanceDir, PropertiesFileName);
        var properties = PropertiesFile.Load(path);
        var propertiesChanged = false;
        var rules = new Dictionary<string, bool>(metadata.GameRules);

        void Property<T>(string key, T before, T after, Func<T, string> format)
        {
            if (!EqualityComparer<T>.Default.Equals(before, after))
            {
                properties.Set(key, format(after));
                propertiesChanged = true;
            }
        }

        void Flag(RuleSetting setting, string key, bool before, bool after)
        {
            if (features.IsGameRule(setting))
            {
                if (before != after)
                {
                    rules[setting.ToString()] = after;
                }
            }
            else
            {
                Property(key, before, after, FormatBool);
            }
        }

        Property("gamemode", loaded.GameMode, edited.GameMode, FormatEnum);
        Property("difficulty", loaded.Difficulty, edited.Difficulty, FormatEnum);
        Flag(RuleSetting.Pvp, "pvp", loaded.Pvp, edited.Pvp);
        Property("max-players", loaded.MaxPlayers, edited.MaxPlayers, FormatInt);
        Property("motd", loaded.Motd, edited.Motd, s => s);
        Flag(RuleSetting.KeepInventory, string.Empty, loaded.KeepInventory, edited.KeepInventory);
        Property("view-distance", loaded.ViewDistance, edited.ViewDistance, FormatInt);
        if (features.HasSimulationDistance)
        {
            Property("simulation-distance", loaded.SimulationDistance, edited.SimulationDistance, FormatInt);
        }

        Property("spawn-protection", loaded.SpawnProtection, edited.SpawnProtection, FormatInt);
        Flag(RuleSetting.CommandBlocks, "enable-command-block", loaded.CommandBlocks, edited.CommandBlocks);
        Property("allow-flight", loaded.AllowFlight, edited.AllowFlight, FormatBool);
        Flag(RuleSetting.SpawnMonsters, "spawn-monsters", loaded.SpawnMonsters, edited.SpawnMonsters);
        Property("hardcore", loaded.Hardcore, edited.Hardcore, FormatBool);
        Property("level-seed", loaded.Seed, edited.Seed, s => s.Trim());

        if (propertiesChanged)
        {
            properties.Save(path);
        }

        return metadata with { GameRules = rules };
    }

    /// <summary>The "gamerule" commands that apply every game rule the user set, for when the server comes online.</summary>
    public static IReadOnlyList<string> RuleCommands(InstanceMetadata metadata)
    {
        var features = GameFeatures.For(metadata.MinecraftVersion);
        var commands = new List<string>();
        foreach (var setting in Enum.GetValues<RuleSetting>())
        {
            if (metadata.GameRules.TryGetValue(setting.ToString(), out var value) && features.RuleName(setting) is { } name)
            {
                commands.Add(RuleCommand(name, value));
            }
        }

        return commands;
    }

    /// <summary>The "gamerule" commands for the rules that differ between two sets of settings, to send right away.</summary>
    public static IReadOnlyList<string> ChangedRuleCommands(InstanceMetadata metadata, GameSettings before, GameSettings after)
    {
        var features = GameFeatures.For(metadata.MinecraftVersion);
        var commands = new List<string>();
        foreach (var (setting, was, now) in RuleValues(before, after))
        {
            if (was != now && features.RuleName(setting) is { } name)
            {
                commands.Add(RuleCommand(name, now));
            }
        }

        return commands;
    }

    /// <summary>Writes the server.properties a new server starts with. Minecraft adds every other key on its first start.</summary>
    public static void WriteDefaults(string instanceDir, string serverName)
    {
        var properties = PropertiesFile.Parse(string.Empty);
        properties.Set("motd", serverName);

        // 26.x turns the whitelist on by default, which with an empty list lets nobody in. Friends should be
        // able to join a new server; the Players tab turns it on with a list.
        properties.Set("white-list", "false");
        properties.Save(Path.Combine(instanceDir, PropertiesFileName));
    }

    private static IEnumerable<(RuleSetting Setting, bool Before, bool After)> RuleValues(GameSettings before, GameSettings after)
    {
        yield return (RuleSetting.KeepInventory, before.KeepInventory, after.KeepInventory);
        yield return (RuleSetting.Pvp, before.Pvp, after.Pvp);
        yield return (RuleSetting.CommandBlocks, before.CommandBlocks, after.CommandBlocks);
        yield return (RuleSetting.SpawnMonsters, before.SpawnMonsters, after.SpawnMonsters);
    }

    private static string RuleCommand(string name, bool value) => $"gamerule {name} {FormatBool(value)}";

    private static string LevelName(string instanceDir)
    {
        var name = PropertiesFile.Load(Path.Combine(instanceDir, PropertiesFileName)).Get("level-name");
        return string.IsNullOrWhiteSpace(name) ? "world" : name;
    }

    private static string FormatBool(bool value) => value ? "true" : "false";

    private static string FormatInt(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string FormatEnum<T>(T value)
        where T : struct, Enum => value.ToString().ToLowerInvariant();

    private static bool? ParseBool(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        "true" => true,
        "false" => false,
        _ => null,
    };

    private static int? ParseInt(string? text) =>
        int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    /// <summary>Minecraft accepts a name ("creative") or the old number ("1").</summary>
    private static T? ParseEnum<T>(string? text)
        where T : struct, Enum
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            return Enum.IsDefined(typeof(T), number) ? (T)(object)number : null;
        }

        return Enum.TryParse<T>(trimmed, ignoreCase: true, out var value) && Enum.IsDefined(value) ? value : null;
    }
}
