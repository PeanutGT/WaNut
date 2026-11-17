using System.Text.Json.Serialization;

namespace WaNut.Core.Bridge;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    Converters = [typeof(CamelCaseEnumConverter<DiscordPresentation>)])]
[JsonSerializable(typeof(BridgeConfig))]
internal sealed partial class BridgeJsonContext : JsonSerializerContext;
