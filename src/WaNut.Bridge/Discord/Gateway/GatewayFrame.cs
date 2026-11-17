using System.Text.Json;

namespace WaNut.Bridge.Discord.Gateway;

/// <summary>One Gateway message: opcode, sequence number, event name and the event's data.</summary>
public sealed record GatewayFrame(int Op, int? Sequence, string? EventName, JsonElement Data)
{
    /// <exception cref="JsonException">Not a Gateway message.</exception>
    public static GatewayFrame Parse(string text)
    {
        using var json = JsonDocument.Parse(text);
        var root = json.RootElement;
        return new GatewayFrame(
            root.GetProperty("op").GetInt32(),
            root.TryGetProperty("s", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt32() : null,
            root.TryGetProperty("t", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null,
            root.TryGetProperty("d", out var d) ? d.Clone() : default);
    }
}
