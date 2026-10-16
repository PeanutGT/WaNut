using System.Text.Json;
using System.Text.Json.Nodes;

namespace WaNut.Core.Settings;

/// <summary>
/// The seed and terrain of a world that does not exist yet. Minecraft reads them only when it first generates
/// the world, so they are written once, when the server is created.
/// </summary>
/// <param name="Seed">Empty means random.</param>
/// <param name="CustomSettings">
/// Advanced mode: raw "generator-settings" JSON for a Superflat world, used instead of <paramref name="Preset"/>.
/// Its format changes between Minecraft versions, so it is passed on as is (only checked to be a JSON object).
/// </param>
public sealed record WorldGeneration(
    string Seed = "",
    WorldType Type = WorldType.Normal,
    FlatPreset Preset = FlatPreset.Classic,
    string CustomSettings = "")
{
    // Before 1.18.2 each structure carried its own placement; these are Minecraft's defaults for them.
    private static readonly (string Id, int Spacing, int Separation, int Salt)[] OldDesertStructures =
    [
        ("minecraft:village", 32, 8, 10387312),
        ("minecraft:desert_pyramid", 32, 8, 14357617),
    ];

    /// <summary>True when <paramref name="json"/> is empty or a JSON object.</summary>
    public static bool IsValidCustomSettings(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return true;
        }

        try
        {
            return JsonNode.Parse(json) is JsonObject;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// The server.properties keys for this world on <paramref name="minecraftVersion"/>. Keys left at Minecraft's
    /// default (a random seed, a Normal world, the Classic flat layers) are not written.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> Properties(string minecraftVersion)
    {
        var features = GameFeatures.For(minecraftVersion);
        var properties = new List<KeyValuePair<string, string>>();
        if (Seed.Trim().Length > 0)
        {
            properties.Add(new("level-seed", Seed.Trim()));
        }

        if (Type != WorldType.Normal)
        {
            properties.Add(new("level-type", LevelType(Type, features.HasWorldPresets)));
        }

        if (Type == WorldType.Superflat && FlatSettings(features) is { } settings)
        {
            properties.Add(new("generator-settings", settings));
        }

        return properties;
    }

    // Up to 1.18.2 the server matches these names ignoring case; from 1.19 level-type is a world preset id.
    private static string LevelType(WorldType type, bool presetIds) => (type, presetIds) switch
    {
        (WorldType.Superflat, true) => "minecraft:flat",
        (WorldType.Superflat, false) => "flat",
        (WorldType.LargeBiomes, true) => "minecraft:large_biomes",
        (WorldType.LargeBiomes, false) => "largebiomes",
        (WorldType.Amplified, true) => "minecraft:amplified",
        (WorldType.Amplified, false) => "amplified",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    private string? FlatSettings(GameFeatures features)
    {
        if (CustomSettings.Trim().Length > 0)
        {
            // One line, so the value stays readable in server.properties.
            return JsonNode.Parse(CustomSettings)!.ToJsonString();
        }

        // Classic is Minecraft's own default for a Superflat world on every version.
        return Preset switch
        {
            FlatPreset.Classic => null,
            FlatPreset.Desert => Flat(
                features,
                "minecraft:desert",
                [("minecraft:bedrock", 1), ("minecraft:stone", 3), ("minecraft:sandstone", 52), ("minecraft:sand", 8)],
                ["minecraft:villages", "minecraft:desert_pyramids", "minecraft:mineshafts", "minecraft:strongholds"],
                OldDesertStructures),
            FlatPreset.Void => Flat(features, "minecraft:the_void", [("minecraft:air", 1)], [], []),
            _ => throw new InvalidOperationException($"Unknown flat preset {Preset}."),
        };
    }

    /// <summary>
    /// Minecraft's flat generator JSON. "features" places the biome's decorations, and in The Void the small
    /// platform at spawn. Structures are a list of structure sets from 1.18.2, and a placement map before that.
    /// </summary>
    private static string Flat(
        GameFeatures features,
        string biome,
        (string Block, int Height)[] layers,
        string[] structureSets,
        (string Id, int Spacing, int Separation, int Salt)[] oldStructures)
    {
        var json = new JsonObject
        {
            ["layers"] = new JsonArray(layers.Select(l => (JsonNode?)new JsonObject { ["block"] = l.Block, ["height"] = l.Height }).ToArray()),
            ["biome"] = biome,
            ["features"] = true,
            ["lakes"] = false,
        };

        if (features.HasStructureSets)
        {
            json["structure_overrides"] = new JsonArray(structureSets.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray());
        }
        else
        {
            var structures = new JsonObject();
            foreach (var (id, spacing, separation, salt) in oldStructures)
            {
                structures[id] = new JsonObject { ["spacing"] = spacing, ["separation"] = separation, ["salt"] = salt };
            }

            // Required before 1.18.2: without it the server ignores the whole value and makes Classic layers.
            json["structures"] = new JsonObject { ["structures"] = structures };
        }

        return json.ToJsonString();
    }
}
