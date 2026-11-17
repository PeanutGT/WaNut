using System.Text.Json;

namespace WaNut.Core.Bridge;

/// <summary>
/// Reads and writes <c>bridge.config.json</c>, the same way on this PC and on a VPS. Uses generated serializers so the
/// trimmed bridge executable can read the file too.
/// </summary>
public static class BridgeConfigJson
{
    public const string FileName = "bridge.config.json";

    public static string Serialize(BridgeConfig config) => JsonSerializer.Serialize(config, BridgeJsonContext.Default.BridgeConfig);

    /// <exception cref="JsonException">The text is not a bridge config.</exception>
    public static BridgeConfig Deserialize(string json) =>
        JsonSerializer.Deserialize(json, BridgeJsonContext.Default.BridgeConfig) ?? throw new JsonException("The bridge config is empty.");
}
