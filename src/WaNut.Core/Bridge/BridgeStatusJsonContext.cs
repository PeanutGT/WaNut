using System.Text.Json.Serialization;

namespace WaNut.Core.Bridge;

/// <summary>One line of JSON per status answer on the bridge's pipe.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Converters = [typeof(CamelCaseEnumConverter<BridgeState>)])]
[JsonSerializable(typeof(BridgeStatus))]
internal sealed partial class BridgeStatusJsonContext : JsonSerializerContext;
