using System.Globalization;
using WaNut.Bridge.Discord;
using WaNut.Core.Notifications;

namespace WaNut.Bridge.Relay;

/// <summary>What the status board shows. <see cref="Players"/> is null while RCON is down (server off).</summary>
public sealed record StatusBoardData(PlayerList? Players, TickStats? Ticks, TimeSpan? Uptime);

/// <summary>The embed the status board keeps editing, as the JSON body of a Discord message.</summary>
public static class StatusBoardMessage
{
    public const int OnlineColor = 0x57F287;

    public const int OfflineColor = 0x99AAB5;

    public static string Build(StatusBoardData data, bool chinese, DateTimeOffset now) => JsonText.Write(json =>
    {
        var online = data.Players is not null;
        json.WriteStartObject();
        json.WriteStartArray("embeds");
        json.WriteStartObject();
        json.WriteString("title", chinese ? "伺服器狀態" : "Server status");
        json.WriteNumber("color", online ? OnlineColor : OfflineColor);
        json.WriteString("timestamp", now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
        json.WriteStartArray("fields");
        Field(json, chinese ? "狀態" : "Status", online ? (chinese ? "🟢 運作中" : "🟢 Online") : (chinese ? "⚫ 未開啟" : "⚫ Offline"));
        if (data.Players is { } players)
        {
            var names = players.Names.Count == 0 ? string.Empty : "\n" + DiscordWebhook.Escape(string.Join(", ", players.Names));
            Field(json, chinese ? "玩家" : "Players", $"{players.Online} / {players.Max}{names}");
            if (data.Ticks is { } ticks)
            {
                if (ticks.Tps is { } tps)
                {
                    Field(json, "TPS", tps.ToString("0.0", CultureInfo.InvariantCulture));
                }

                if (ticks.Mspt is { } mspt)
                {
                    Field(json, "MSPT", mspt.ToString("0.0", CultureInfo.InvariantCulture) + " ms");
                }
            }

            if (data.Uptime is { } up)
            {
                Field(json, chinese ? "運行時間" : "Uptime", FormatUptime(up, chinese));
            }
        }

        json.WriteEndArray();
        json.WriteEndObject();
        json.WriteEndArray();
        JsonText.WriteNoMentions(json);
        json.WriteEndObject();
    });

    public static string FormatUptime(TimeSpan span, bool chinese)
    {
        var days = (int)span.TotalDays;
        return (days, span.Hours) switch
        {
            ( > 0, _) => chinese ? $"{days} 天 {span.Hours} 小時" : $"{days}d {span.Hours}h",
            (_, > 0) => chinese ? $"{span.Hours} 小時 {span.Minutes} 分" : $"{span.Hours}h {span.Minutes}m",
            _ => chinese ? $"{span.Minutes} 分" : $"{span.Minutes}m",
        };
    }

    private static void Field(System.Text.Json.Utf8JsonWriter json, string name, string value)
    {
        json.WriteStartObject();
        json.WriteString("name", name);
        json.WriteString("value", value);
        json.WriteBoolean("inline", true);
        json.WriteEndObject();
    }
}
