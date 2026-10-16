using WaNut.Core.Instances;
using WaNut.Core.Settings;

namespace WaNut.Core.Worlds;

/// <summary>
/// Where a world keeps its Nether and End, and its player files. Paper up to 1.21.x keeps the Nether and the End in
/// their own folders ("world_nether/DIM-1", "world_the_end/DIM1"); Vanilla and Fabric keep them inside the world
/// ("world/DIM-1", "world/DIM1"). From 26.x every type keeps all dimensions inside the world.
/// </summary>
public static class WorldLayout
{
    private static readonly (string Suffix, string Dimension)[] Dimensions = [("_nether", "DIM-1"), ("_the_end", "DIM1")];

    /// <summary>Minecraft 1.x (up to 1.21.x); the year-based versions (26.x) use the new world layout.</summary>
    public static bool IsLegacyVersion(string minecraftVersion) => GameFeatures.ParseVersion(minecraftVersion).Major == 1;

    public static bool SplitsDimensions(ServerType type, string minecraftVersion) => type == ServerType.Paper && IsLegacyVersion(minecraftVersion);

    /// <summary>Moves DIM-1 and DIM1 out of <paramref name="level"/> into Paper's _nether and _the_end folders.</summary>
    public static void Split(string parentDir, string level) => Move(parentDir, level, toSplit: true);

    /// <summary>Moves Paper's _nether/DIM-1 and _the_end/DIM1 back inside <paramref name="level"/>.</summary>
    public static void Join(string parentDir, string level) => Move(parentDir, level, toSplit: false);

    /// <summary>
    /// The folder the server keeps each player's inventory in. A folder the world already has wins; otherwise
    /// "playerdata", or "players/data" from 26.x, where Minecraft moved the player files.
    /// </summary>
    public static string PlayerDataDir(string worldDir, string minecraftVersion)
    {
        var old = Path.Combine(worldDir, "playerdata");
        var current = Path.Combine(worldDir, "players", "data");
        if (Directory.Exists(current))
        {
            return current;
        }

        return Directory.Exists(old) || IsLegacyVersion(minecraftVersion) ? old : current;
    }

    /// <summary>The player file for <paramref name="uuid"/> in whichever player folder the world has, or null.</summary>
    public static string? FindPlayerFile(string worldDir, Guid uuid)
    {
        var name = uuid.ToString("D") + ".dat";
        return new[] { Path.Combine(worldDir, "players", "data", name), Path.Combine(worldDir, "playerdata", name) }
            .FirstOrDefault(File.Exists);
    }

    private static void Move(string parentDir, string level, bool toSplit)
    {
        foreach (var (suffix, dimension) in Dimensions)
        {
            var inside = Path.Combine(parentDir, level, dimension);
            var split = Path.Combine(parentDir, level + suffix, dimension);
            var (from, to) = toSplit ? (inside, split) : (split, inside);
            if (!Directory.Exists(from))
            {
                continue;
            }

            // A leftover folder from an earlier switch is older than the one being moved; callers back it up first.
            if (Directory.Exists(to))
            {
                Directory.Delete(to, recursive: true);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            Directory.Move(from, to);
        }
    }
}
