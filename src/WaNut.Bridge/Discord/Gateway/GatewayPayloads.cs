using System.Text.Json;

namespace WaNut.Bridge.Discord.Gateway;

/// <summary>The messages the bridge sends to the Gateway.</summary>
public static class GatewayPayloads
{
    /// <summary>Guild messages and their content. Slash commands arrive without an intent.</summary>
    public const int Intents = (1 << 9) | (1 << 15);

    public static string Heartbeat(int? sequence) => JsonText.Write(json =>
    {
        json.WriteStartObject();
        json.WriteNumber("op", GatewayOpcode.Heartbeat);
        if (sequence is { } s)
        {
            json.WriteNumber("d", s);
        }
        else
        {
            json.WriteNull("d");
        }

        json.WriteEndObject();
    });

    public static string Identify(string token, string? presence) => JsonText.Write(json =>
    {
        json.WriteStartObject();
        json.WriteNumber("op", GatewayOpcode.Identify);
        json.WriteStartObject("d");
        json.WriteString("token", token);
        json.WriteNumber("intents", Intents);
        json.WriteStartObject("properties");
        json.WriteString("os", OperatingSystem.IsWindows() ? "windows" : "linux");
        json.WriteString("browser", "WaNut");
        json.WriteString("device", "WaNut");
        json.WriteEndObject();
        if (presence is not null)
        {
            json.WritePropertyName("presence");
            WritePresence(json, presence);
        }

        json.WriteEndObject();
        json.WriteEndObject();
    });

    public static string Resume(string token, string sessionId, int? sequence) => JsonText.Write(json =>
    {
        json.WriteStartObject();
        json.WriteNumber("op", GatewayOpcode.Resume);
        json.WriteStartObject("d");
        json.WriteString("token", token);
        json.WriteString("session_id", sessionId);
        json.WriteNumber("seq", sequence ?? 0);
        json.WriteEndObject();
        json.WriteEndObject();
    });

    /// <summary>"Playing &lt;text&gt;" under the bot's name.</summary>
    public static string Presence(string text) => JsonText.Write(json =>
    {
        json.WriteStartObject();
        json.WriteNumber("op", GatewayOpcode.PresenceUpdate);
        json.WritePropertyName("d");
        WritePresence(json, text);
        json.WriteEndObject();
    });

    private static void WritePresence(Utf8JsonWriter json, string text)
    {
        json.WriteStartObject();
        json.WriteNull("since");
        json.WriteStartArray("activities");
        json.WriteStartObject();
        json.WriteString("name", text);
        json.WriteNumber("type", 0);
        json.WriteEndObject();
        json.WriteEndArray();
        json.WriteString("status", "online");
        json.WriteBoolean("afk", false);
        json.WriteEndObject();
    }
}
