using System.Text.Json;

namespace WaNut.Bridge.Discord;

/// <summary>
/// The slash commands the bridge registers in the Discord server: /list for everyone, /status and, when the remote console is
/// on, /cmd. /cmd and /status are registered as admin-only (default_member_permissions "0"), so Discord hides it from everyone
/// else until a server admin allows a role under Server Settings > Integrations; the bridge also checks the admin
/// role itself. Descriptions carry both English and Traditional Chinese, shown by each user's Discord language.
/// </summary>
public static class SlashCommandDefinitions
{
    public const string List = "list";

    public const string Console = "cmd";

    public const string Status = "status";

    public const string ConsoleOption = "command";

    /// <summary>The body of "bulk overwrite guild commands": commands not in it are removed.</summary>
    public static string Build(bool list, bool console, bool status = true) => JsonText.Write(json =>
    {
        json.WriteStartArray();
        if (list)
        {
            json.WriteStartObject();
            WriteHeader(json, List, "Show who is online in Minecraft", "查看 Minecraft 線上玩家");
            json.WriteEndObject();
        }

        if (console)
        {
            json.WriteStartObject();
            WriteHeader(json, Console, "Run a Minecraft server command (admins)", "執行 Minecraft 伺服器指令（管理員）");
            json.WriteString("default_member_permissions", "0");
            json.WriteStartArray("options");
            json.WriteStartObject();
            json.WriteNumber("type", 3);
            json.WriteString("name", ConsoleOption);
            WriteDescription(json, "The command, without the leading /", "要執行的指令，不用加 /");
            json.WriteBoolean("required", true);
            json.WriteEndObject();
            json.WriteEndArray();
            json.WriteEndObject();
        }

        if (status)
        {
            json.WriteStartObject();
            WriteHeader(json, Status, "Put the live server status message in this channel (admins)", "在這個頻道放上自動更新的伺服器狀態訊息（管理員）");
            json.WriteString("default_member_permissions", "0");
            json.WriteEndObject();
        }

        json.WriteEndArray();
    });

    private static void WriteHeader(Utf8JsonWriter json, string name, string english, string chinese)
    {
        json.WriteString("name", name);
        json.WriteNumber("type", 1);
        WriteDescription(json, english, chinese);

        // Server channels only, never direct messages.
        json.WriteStartArray("contexts");
        json.WriteNumberValue(0);
        json.WriteEndArray();
    }

    private static void WriteDescription(Utf8JsonWriter json, string english, string chinese)
    {
        json.WriteString("description", english);
        json.WriteStartObject("description_localizations");
        json.WriteString("zh-TW", chinese);
        json.WriteEndObject();
    }
}
