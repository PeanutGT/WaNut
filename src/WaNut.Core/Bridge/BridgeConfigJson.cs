using System.Text.Json;
using System.Text.Json.Serialization;

namespace WaNut.Core.Bridge;

/// <summary>Reads and writes <c>bridge.config.json</c>, the same way on this PC and on a VPS.</summary>
public static class BridgeConfigJson
{
    public const string FileName = "bridge.config.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Serialize(BridgeConfig config) => JsonSerializer.Serialize(config, Options);

    /// <exception cref="JsonException">The text is not a bridge config.</exception>
    public static BridgeConfig Deserialize(string json) =>
        JsonSerializer.Deserialize<BridgeConfig>(json, Options) ?? throw new JsonException("The bridge config is empty.");
}
