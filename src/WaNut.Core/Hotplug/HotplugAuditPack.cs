using System.Text.Json.Serialization;

namespace WaNut.Core.Hotplug;

public sealed record HotplugAuditPack(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("action")] HotplugSwapResult Action,
    [property: JsonPropertyName("sha256")] string? Sha256);
