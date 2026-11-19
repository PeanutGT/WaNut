using WaNut.Core.Settings;

namespace WaNut.Core.Hotplug.Preflight;

/// <summary>The data pack format (pack.mcmeta's pack_format) each Minecraft release expects.</summary>
public static class DatapackFormats
{
    // First release of each format, oldest first. A version takes the format of the last entry at or below it.
    private static readonly (Version From, int Format)[] Table =
    [
        (new(1, 13), 4),
        (new(1, 15), 5),
        (new(1, 16, 2), 6),
        (new(1, 17), 7),
        (new(1, 18), 8),
        (new(1, 18, 2), 9),
        (new(1, 19), 10),
        (new(1, 19, 4), 12),
        (new(1, 20), 15),
        (new(1, 20, 2), 18),
        (new(1, 20, 3), 26),
        (new(1, 20, 5), 41),
        (new(1, 21), 48),
        (new(1, 21, 2), 57),
        (new(1, 21, 4), 61),
        (new(1, 21, 5), 71),
        (new(1, 21, 6), 80),
        (new(1, 21, 7), 81),
        (new(1, 21, 9), 88),
    ];

    // The newest release the table is known to cover; anything later may have a newer format.
    private static readonly Version LastKnown = new(1, 21, 10);

    /// <summary>The major format for <paramref name="minecraftVersion"/>, or null when it is older or newer than the table (or a snapshot).</summary>
    public static int? For(string minecraftVersion)
    {
        Version version;
        try
        {
            version = GameFeatures.ParseVersion(minecraftVersion);
        }
        catch (FormatException)
        {
            return null;
        }

        if (version < Table[0].From || version > LastKnown)
        {
            return null;
        }

        return Table.Last(t => t.From <= version).Format;
    }

    /// <summary>Compares what the pack supports with what the server expects.</summary>
    public static FormatCheck Check(PackMetadata metadata, int? expected)
    {
        if (expected is not { } want || (metadata.MinFormat is null && metadata.MaxFormat is null))
        {
            return FormatCheck.Unknown;
        }

        var min = metadata.MinFormat ?? metadata.MaxFormat!.Value;
        var max = metadata.MaxFormat ?? metadata.MinFormat!.Value;
        return want >= min && want <= max ? FormatCheck.Matches : FormatCheck.Mismatch;
    }
}
