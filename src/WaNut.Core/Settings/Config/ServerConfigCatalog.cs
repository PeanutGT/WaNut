namespace WaNut.Core.Settings.Config;

/// <summary>
/// Every server.properties key WaNut knows: its section, control, valid range and default (the vanilla default).
/// Order here is the order on the page.
/// </summary>
public static class ServerConfigCatalog
{
    private static readonly Version Since118 = new(1, 18, 0);

    private static readonly IReadOnlyList<ConfigOption> GameModes = Options("survival", "creative", "adventure", "spectator");
    private static readonly IReadOnlyList<ConfigOption> Difficulties = Options("peaceful", "easy", "normal", "hard");

    public static IReadOnlyList<ConfigMetadata> All { get; } =
    [
        // Core
        new("gamemode", ConfigSection.Core, ConfigControlType.Dropdown, "survival", GameModes),
        new("difficulty", ConfigSection.Core, ConfigControlType.Dropdown, "easy", Difficulties),
        new("max-players", ConfigSection.Core, ConfigControlType.NumberInput, "20", Min: 1, Max: int.MaxValue),
        new("online-mode", ConfigSection.Core, ConfigControlType.Toggle, "true",
            WarningBannerKey: "ServerConfig_OnlineMode_Warning", WarningTriggerValue: "false") { RequiresRestart = true },
        new("pvp", ConfigSection.Core, ConfigControlType.Toggle, "true") { GameRule = RuleSetting.Pvp },
        new("keepInventory", ConfigSection.Core, ConfigControlType.Toggle, "false") { GameRule = RuleSetting.KeepInventory },
        new("motd", ConfigSection.Core, ConfigControlType.Text, "A Minecraft Server"),

        // World and rules
        new("allow-flight", ConfigSection.WorldRules, ConfigControlType.Toggle, "false"),
        new("spawn-monsters", ConfigSection.WorldRules, ConfigControlType.Toggle, "true") { GameRule = RuleSetting.SpawnMonsters },
        new("spawn-animals", ConfigSection.WorldRules, ConfigControlType.Toggle, "true"),
        new("spawn-npcs", ConfigSection.WorldRules, ConfigControlType.Toggle, "true"),
        new("enable-command-block", ConfigSection.WorldRules, ConfigControlType.Toggle, "false") { GameRule = RuleSetting.CommandBlocks },
        new("spawn-protection", ConfigSection.WorldRules, ConfigControlType.NumberInput, "16", Min: 0, Max: 1000),
        new("hardcore", ConfigSection.WorldRules, ConfigControlType.Toggle, "false") { RequiresRestart = true },
        new("force-gamemode", ConfigSection.WorldRules, ConfigControlType.Toggle, "false"),
        new("generate-structures", ConfigSection.WorldRules, ConfigControlType.Toggle, "true") { RequiresRestart = true },
        new("allow-nether", ConfigSection.WorldRules, ConfigControlType.Toggle, "true") { RequiresRestart = true },
        new("level-seed", ConfigSection.WorldRules, ConfigControlType.Text, string.Empty) { RequiresRestart = true },

        // Connection and performance
        new("server-port", ConfigSection.NetworkPerf, ConfigControlType.NumberInput, "25565", Min: 1, Max: 65535) { RequiresRestart = true },
        new("view-distance", ConfigSection.NetworkPerf, ConfigControlType.Slider, "10", Min: 3, Max: 32)
        {
            HintKey = "ServerConfig_ViewDistance_Hint",
            HintAbove = 12,
        },
        new("simulation-distance", ConfigSection.NetworkPerf, ConfigControlType.Slider, "10", Min: 3, Max: 32)
        {
            Since = Since118,
            HintKey = "ServerConfig_SimulationDistance_Hint",
            HintAbove = 12,
        },
        new("white-list", ConfigSection.NetworkPerf, ConfigControlType.Toggle, "false"),
        new("enforce-whitelist", ConfigSection.NetworkPerf, ConfigControlType.Toggle, "false"),

        // Only in the full list
        new("level-name", ConfigSection.RawOnly, ConfigControlType.Text, "world") { RequiresRestart = true },
        new("level-type", ConfigSection.RawOnly, ConfigControlType.Text, "minecraft:normal") { RequiresRestart = true },
        new("generator-settings", ConfigSection.RawOnly, ConfigControlType.Text, "{}") { RequiresRestart = true },
        new("server-ip", ConfigSection.RawOnly, ConfigControlType.Text, string.Empty) { RequiresRestart = true },
        new("enable-status", ConfigSection.RawOnly, ConfigControlType.Toggle, "true"),
        new("hide-online-players", ConfigSection.RawOnly, ConfigControlType.Toggle, "false"),
        new("prevent-proxy-connections", ConfigSection.RawOnly, ConfigControlType.Toggle, "false"),
        new("sync-chunk-writes", ConfigSection.RawOnly, ConfigControlType.Toggle, "true") { RequiresRestart = true },
        new("network-compression-threshold", ConfigSection.RawOnly, ConfigControlType.NumberInput, "256", Min: -1, Max: int.MaxValue) { RequiresRestart = true },
        new("rate-limit", ConfigSection.RawOnly, ConfigControlType.NumberInput, "0", Min: 0, Max: int.MaxValue),
        new("max-tick-time", ConfigSection.RawOnly, ConfigControlType.NumberInput, "60000", Min: -1, Max: long.MaxValue),
        new("max-world-size", ConfigSection.RawOnly, ConfigControlType.NumberInput, "29999984", Min: 1, Max: 29999984) { RequiresRestart = true },
        new("player-idle-timeout", ConfigSection.RawOnly, ConfigControlType.NumberInput, "0", Min: 0, Max: int.MaxValue),
        new("op-permission-level", ConfigSection.RawOnly, ConfigControlType.NumberInput, "4", Min: 0, Max: 4),
        new("function-permission-level", ConfigSection.RawOnly, ConfigControlType.NumberInput, "2", Min: 1, Max: 4),
        new("entity-broadcast-range-percentage", ConfigSection.RawOnly, ConfigControlType.NumberInput, "100", Min: 10, Max: 1000),
        new("require-resource-pack", ConfigSection.RawOnly, ConfigControlType.Toggle, "false"),
        new("resource-pack", ConfigSection.RawOnly, ConfigControlType.Text, string.Empty),
        new("resource-pack-prompt", ConfigSection.RawOnly, ConfigControlType.Text, string.Empty),
        new("resource-pack-sha1", ConfigSection.RawOnly, ConfigControlType.Text, string.Empty),
        new("enable-jmx-monitoring", ConfigSection.RawOnly, ConfigControlType.Toggle, "false") { RequiresRestart = true },
        new("enable-query", ConfigSection.RawOnly, ConfigControlType.Toggle, "false") { RequiresRestart = true },
        new("query.port", ConfigSection.RawOnly, ConfigControlType.NumberInput, "25565", Min: 1, Max: 65535) { RequiresRestart = true },
        new("enable-rcon", ConfigSection.RawOnly, ConfigControlType.Toggle, "false") { RequiresRestart = true },
        new("rcon.port", ConfigSection.RawOnly, ConfigControlType.NumberInput, "25575", Min: 1, Max: 65535) { RequiresRestart = true },
        new("rcon.password", ConfigSection.RawOnly, ConfigControlType.Text, string.Empty) { RequiresRestart = true },
        new("broadcast-console-to-ops", ConfigSection.RawOnly, ConfigControlType.Toggle, "true"),
        new("broadcast-rcon-to-ops", ConfigSection.RawOnly, ConfigControlType.Toggle, "true"),
        new("enable-code-of-conduct", ConfigSection.RawOnly, ConfigControlType.Toggle, "false"),
        new("text-filtering-config", ConfigSection.RawOnly, ConfigControlType.Text, string.Empty),
        new("use-native-transport", ConfigSection.RawOnly, ConfigControlType.Toggle, "true") { RequiresRestart = true },
        new("log-ips", ConfigSection.RawOnly, ConfigControlType.Toggle, "true"),
        new("accepts-transfers", ConfigSection.RawOnly, ConfigControlType.Toggle, "false"),
        new("pause-when-empty-seconds", ConfigSection.RawOnly, ConfigControlType.NumberInput, "60", Min: -1, Max: int.MaxValue),
    ];

    /// <summary>
    /// The entries that apply to a version: simulation-distance only exists from 1.18, and PvP, command blocks and
    /// monster spawning are game rules (not keys) from 1.21.9 and are left to the game rule path.
    /// </summary>
    public static IEnumerable<ConfigMetadata> For(GameFeatures features, string minecraftVersion)
    {
        var version = GameFeatures.ParseVersion(minecraftVersion);
        return All.Where(m => (m.Since is null || version >= m.Since)
            && (m.GameRule is not { } rule || !features.IsGameRule(rule)));
    }

    private static IReadOnlyList<ConfigOption> Options(params string[] values) =>
        values.Select(v => new ConfigOption(v, $"ServerConfig_Option_{char.ToUpperInvariant(v[0])}{v[1..]}")).ToList();
}
