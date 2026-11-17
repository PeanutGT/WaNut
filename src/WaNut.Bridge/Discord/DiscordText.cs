using System.Globalization;
using System.Text.RegularExpressions;

namespace WaNut.Bridge.Discord;

/// <summary>Turns Discord's markup in a message into what a player should read in game.</summary>
public static partial class DiscordText
{
    /// <param name="userNames">Names of the users the message mentions, by id.</param>
    public static string ToPlain(string content, IReadOnlyDictionary<ulong, string> userNames)
    {
        var text = UserMention().Replace(content, m =>
            ulong.TryParse(m.Groups["id"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && userNames.TryGetValue(id, out var name)
                ? "@" + name
                : "@user");
        text = RoleMention().Replace(text, "@role");
        text = ChannelMention().Replace(text, "#channel");
        return CustomEmoji().Replace(text, m => ":" + m.Groups["name"].Value + ":");
    }

    [GeneratedRegex(@"<@!?(?<id>\d+)>")]
    private static partial Regex UserMention();

    [GeneratedRegex(@"<@&\d+>")]
    private static partial Regex RoleMention();

    [GeneratedRegex(@"<#\d+>")]
    private static partial Regex ChannelMention();

    [GeneratedRegex(@"<a?:(?<name>\w+):\d+>")]
    private static partial Regex CustomEmoji();
}
