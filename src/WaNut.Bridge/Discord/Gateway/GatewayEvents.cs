using System.Globalization;
using System.Text.Json;

namespace WaNut.Bridge.Discord.Gateway;

/// <summary>Reads the Gateway events the bridge cares about into its own records.</summary>
public static class GatewayEvents
{
    /// <summary>Normal messages and replies; joins, pins and other system messages are null.</summary>
    public static InboundMessage? Message(JsonElement d)
    {
        var type = Int(d, "type") ?? 0;
        if (type is not (0 or 19) || !d.TryGetProperty("author", out var author))
        {
            return null;
        }

        var member = d.TryGetProperty("member", out var m) ? m : default;
        var names = new Dictionary<ulong, string>();
        if (d.TryGetProperty("mentions", out var mentions) && mentions.ValueKind == JsonValueKind.Array)
        {
            foreach (var user in mentions.EnumerateArray())
            {
                names[Snowflake(user, "id")] = DisplayName(user, user.TryGetProperty("member", out var um) ? um : default);
            }
        }

        var content = DiscordText.ToPlain(String(d, "content") ?? string.Empty, names);
        var files = new List<InboundAttachment>();
        if (d.TryGetProperty("attachments", out var attachments) && attachments.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in attachments.EnumerateArray())
            {
                if (String(a, "url") is { Length: > 0 } url)
                {
                    files.Add(new InboundAttachment(url, String(a, "filename") ?? "file", String(a, "content_type"), a.TryGetProperty("size", out var size) && size.TryGetInt64(out var n) ? n : 0));
                }
            }
        }

        InboundReply? reply = null;
        if (type == 19 && d.TryGetProperty("referenced_message", out var referenced) && referenced.ValueKind == JsonValueKind.Object
            && referenced.TryGetProperty("author", out var referencedAuthor))
        {
            reply = new InboundReply(DisplayName(referencedAuthor, default), DiscordText.ToPlain(String(referenced, "content") ?? string.Empty, names));
        }

        return new InboundMessage(
            Snowflake(d, "channel_id"),
            Snowflake(author, "id"),
            DisplayName(author, member),
            author.TryGetProperty("bot", out var bot) && bot.ValueKind == JsonValueKind.True,
            d.TryGetProperty("webhook_id", out var webhook) && webhook.ValueKind == JsonValueKind.String,
            Roles(member),
            content,
            files,
            reply);
    }

    /// <summary>A slash command used in a server; buttons, autocomplete and direct messages are null.</summary>
    public static SlashCommand? Command(JsonElement d)
    {
        if (Int(d, "type") != 2 || !d.TryGetProperty("member", out var member) || !member.TryGetProperty("user", out var user)
            || !d.TryGetProperty("data", out var data))
        {
            return null;
        }

        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        if (data.TryGetProperty("options", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var option in list.EnumerateArray())
            {
                if (String(option, "name") is { } name && option.TryGetProperty("value", out var value))
                {
                    options[name] = value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();
                }
            }
        }

        return new SlashCommand(
            String(d, "id") ?? string.Empty,
            String(d, "token") ?? string.Empty,
            String(d, "application_id") ?? string.Empty,
            Snowflake(d, "channel_id"),
            Snowflake(user, "id"),
            DisplayName(user, member),
            Roles(member),
            String(data, "name") ?? string.Empty,
            options);
    }

    private static string DisplayName(JsonElement user, JsonElement member) =>
        (member.ValueKind == JsonValueKind.Object ? String(member, "nick") : null) is { Length: > 0 } nick ? nick
        : String(user, "global_name") is { Length: > 0 } globalName ? globalName
        : String(user, "username") ?? "?";

    private static ulong[] Roles(JsonElement member) =>
        member.ValueKind == JsonValueKind.Object && member.TryGetProperty("roles", out var roles) && roles.ValueKind == JsonValueKind.Array
            ? [.. roles.EnumerateArray().Select(r => ulong.TryParse(r.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : 0)]
            : [];

    internal static string? String(JsonElement e, string name) =>
        e.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;

    internal static ulong Snowflake(JsonElement e, string name) =>
        ulong.TryParse(String(e, name), NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : 0;
}
