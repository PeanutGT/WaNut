using System.Text.Json.Serialization;

namespace WaNut.Bridge.Discord;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DiscordPost))]
internal sealed partial class DiscordJsonContext : JsonSerializerContext;
