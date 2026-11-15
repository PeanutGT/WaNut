using WaNut.Core.Instances;

namespace WaNut.Core.Settings;

/// <summary>
/// Paper's "lighter entities" switch: a few world settings that save CPU with many mobs or villagers around.
/// Each changes the game a little (fewer mobs pushing each other, villagers checking their beds and work places less
/// often), so it stays off unless the user turns it on. Off puts Paper's own defaults back.
/// </summary>
public static class PaperTweaks
{
    /// <summary>Paper 1.19 and later keep the world settings here, written on the first start.</summary>
    public static readonly string RelativePath = Path.Combine("config", "paper-world-defaults.yml");

    private static readonly (string Path, string Lighter, string Default)[] Values =
    [
        // Mobs pushing each other: crowded farms and pens use much less CPU.
        ("collisions.max-entity-collisions", "2", "8"),

        // Explosions next to each other (TNT) are worked out together.
        ("environment.optimize-explosions", "true", "false"),

        // Villagers look for their work place and bed every 3 and 4 seconds instead of every tick and 2 seconds.
        ("tick-rates.behavior.villager.validatenearbypoi", "60", "-1"),
        ("tick-rates.sensor.villager.secondarypoisensor", "80", "40"),
    ];

    public static bool Supports(ServerType type, string minecraftVersion) =>
        type == ServerType.Paper && GameFeatures.ParseVersion(minecraftVersion) >= new Version(1, 19);

    public static string PathIn(string instanceDir) => Path.Combine(instanceDir, RelativePath);

    /// <summary>False before the first start, when Paper has not written the file yet.</summary>
    public static bool HasFile(string instanceDir) => File.Exists(PathIn(instanceDir));

    public static bool IsOn(string instanceDir) =>
        HasFile(instanceDir) && IsOnIn(File.ReadAllText(PathIn(instanceDir)));

    public static bool IsOnIn(string yaml) => Values.All(v => YamlScalars.Get(yaml, v.Path) == v.Lighter);

    /// <summary>Only call while the server is stopped: Paper writes the file back when it stops.</summary>
    public static void Set(string instanceDir, bool on)
    {
        var path = PathIn(instanceDir);
        var text = Apply(File.ReadAllText(path), on);
        var temp = path + ".tmp";
        File.WriteAllText(temp, text);
        File.Move(temp, path, overwrite: true);
    }

    public static string Apply(string yaml, bool on) =>
        Values.Aggregate(yaml, (text, v) => YamlScalars.Set(text, v.Path, on ? v.Lighter : v.Default));
}
