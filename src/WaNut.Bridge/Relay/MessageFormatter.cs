using WaNut.Bridge.Discord;
using WaNut.Bridge.GameLog;
using WaNut.Core.Bridge;
using WaNut.Core.Notifications;

namespace WaNut.Bridge.Relay;

/// <summary>Turns a game event into a Discord line in the look the user chose.</summary>
public static class MessageFormatter
{
    public const int ChatColor = 0x99AAB5;

    public const int JoinColor = 0x57F287;

    public const int LeaveColor = 0xED4245;

    public const int DeathColor = 0x992D22;

    public const int AdvancementColor = 0xF1C40F;

    public const int ServerColor = 0x5865F2;

    public const int CrashColor = 0xED4245;

    /// <summary>The player's head; names are checked to be plain letters, digits and underscores before they get here.</summary>
    public static string AvatarUrl(string player) => $"https://mc-heads.net/avatar/{player}/64";

    public static OutgoingMessage Format(GameEvent e, DiscordPresentation presentation, BridgeTexts texts)
    {
        var player = DiscordWebhook.Escape(e.Player ?? string.Empty);
        if (e.Kind == GameEventKind.Chat)
        {
            var text = DiscordWebhook.Escape(e.Text ?? string.Empty);
            return presentation switch
            {
                DiscordPresentation.Webhook when IsAllowedWebhookName(e.Player) => new OutgoingMessage(text, e.Player, AvatarUrl(e.Player!)),
                DiscordPresentation.Webhook => new OutgoingMessage($"**{player}**: {text}"),
                DiscordPresentation.Embed => new OutgoingMessage($"**{player}**: {text}", EmbedColor: ChatColor),
                _ => new OutgoingMessage($"[MC] <{player}> {text}"),
            };
        }

        var (line, color) = e.Kind switch
        {
            GameEventKind.Joined => (texts.Joined(e.Player!), JoinColor),
            GameEventKind.Left => (texts.Left(e.Player!), LeaveColor),
            GameEventKind.Death => (e.Text!, DeathColor),
            GameEventKind.Advancement => (e.Text!, AdvancementColor),
            GameEventKind.ServerStarted => (texts.ServerStarted, ServerColor),
            _ => (texts.ServerStopping, ServerColor),
        };
        var escaped = DiscordWebhook.Escape(line);
        return presentation switch
        {
            DiscordPresentation.Webhook => new OutgoingMessage($"**{escaped}**"),
            DiscordPresentation.Embed => new OutgoingMessage(escaped, EmbedColor: color),
            _ => new OutgoingMessage($"[MC] {escaped}"),
        };
    }

    /// <summary>Discord refuses webhook names containing "discord" or "clyde", and the names "everyone" and "here".</summary>
    public static bool IsAllowedWebhookName(string? name) =>
        !string.IsNullOrEmpty(name)
        && !name.Contains("discord", StringComparison.OrdinalIgnoreCase)
        && !name.Contains("clyde", StringComparison.OrdinalIgnoreCase)
        && !name.Equals("everyone", StringComparison.OrdinalIgnoreCase)
        && !name.Equals("here", StringComparison.OrdinalIgnoreCase);
}
