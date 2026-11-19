using System.Text.Json;

namespace WaNut.Core.Hotplug.Preflight;

/// <summary>
/// What a data pack's pack.mcmeta says about itself. Formats are major numbers: pack_format, the supported_formats
/// range (1.20.2+) and min_format / max_format (1.21.9+, where a format may be written as [major, minor]).
/// </summary>
/// <param name="MinFormat">The oldest format the pack says it works with; null when it names none.</param>
/// <param name="MaxFormat">The newest format the pack says it works with.</param>
/// <param name="Description">The description when it is plain text.</param>
public sealed record PackMetadata(int? MinFormat, int? MaxFormat, string? Description)
{
    public const string FileName = "pack.mcmeta";

    /// <summary>Reads pack.mcmeta's text; null with a reason when it isn't a pack.mcmeta Minecraft would load.</summary>
    public static PackMetadata? TryParse(string json, out string? problem)
    {
        problem = null;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("pack", out var pack)
                || pack.ValueKind != JsonValueKind.Object)
            {
                problem = "pack.mcmeta has no \"pack\" section.";
                return null;
            }

            var formats = new List<int>();
            if (pack.TryGetProperty("pack_format", out var packFormat) && Major(packFormat) is { } single)
            {
                formats.Add(single);
            }

            if (pack.TryGetProperty("supported_formats", out var supported))
            {
                formats.AddRange(Range(supported));
            }

            int? min = pack.TryGetProperty("min_format", out var minFormat) ? Major(minFormat) : null;
            int? max = pack.TryGetProperty("max_format", out var maxFormat) ? Major(maxFormat) : null;
            if (min is null && max is null && formats.Count == 0)
            {
                problem = "pack.mcmeta names no pack_format.";
                return null;
            }

            var description = pack.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
            return new PackMetadata(min ?? (formats.Count > 0 ? formats.Min() : max), max ?? (formats.Count > 0 ? formats.Max() : min), description);
        }
        catch (JsonException e)
        {
            problem = $"pack.mcmeta is not valid JSON: {e.Message}";
            return null;
        }
    }

    /// <summary>48, or [88, 0] for "88.0".</summary>
    private static int? Major(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number when value.TryGetInt32(out var n) => n,
        JsonValueKind.Number when value.TryGetDouble(out var f) => (int)Math.Floor(f),
        JsonValueKind.Array when value.GetArrayLength() > 0 => Major(value[0]),
        _ => null,
    };

    /// <summary>supported_formats: a number, [min, max], or {"min_inclusive": a, "max_inclusive": b}.</summary>
    private static IEnumerable<int> Range(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Number when Major(value) is { } n:
                yield return n;
                break;
            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray())
                {
                    if (Major(item) is { } m)
                    {
                        yield return m;
                    }
                }

                break;
            case JsonValueKind.Object:
                foreach (var key in new[] { "min_inclusive", "max_inclusive" })
                {
                    if (value.TryGetProperty(key, out var bound) && Major(bound) is { } b)
                    {
                        yield return b;
                    }
                }

                break;
        }
    }
}
