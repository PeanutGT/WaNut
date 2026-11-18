using System.Globalization;
using WaNut.Bridge.Discord;
using WaNut.Core.Bridge;

namespace WaNut.Bridge.Relay;

/// <summary>Decides what a Discord message or slash command should do in game.</summary>
public static class InboundRouter
{
    /// <param name="botUserId">The bridge bot's own id.</param>
    public static InboundAction Route(InboundMessage message, BridgeConfig config, ulong botUserId)
    {
        // Loop guard: the bridge's own posts (bot or webhook) and every other bot or webhook never go back into the game.
        if (!IsBridgeChannel(message.ChannelId, config)
            || message.IsBot
            || message.IsWebhook
            || message.AuthorId == botUserId)
        {
            return InboundAction.Ignore;
        }

        var text = message.Content.Trim();
        if ((text.Length == 0 && message.Files.Count == 0) || !config.Filters.Chat || HasIgnoredPrefix(text, config.Filters))
        {
            return InboundAction.Ignore;
        }

        return new InboundAction(InboundActionKind.Chat, message.AuthorName, text) { Attachments = message.Files, Reply = message.Reply };
    }

    public static InboundAction Route(SlashCommand command, BridgeConfig config)
    {
        var kind = command.Name switch
        {
            SlashCommandDefinitions.List when config.Filters.ListCommand => InboundActionKind.ListPlayers,
            SlashCommandDefinitions.Console when config.Filters.RemoteConsole => InboundActionKind.Console,
            SlashCommandDefinitions.Status => InboundActionKind.Status,
            _ => InboundActionKind.Ignore,
        };
        if (kind == InboundActionKind.Ignore)
        {
            return InboundAction.Ignore;
        }

        // The one command that works in any channel of the server: it is how the board gets there.
        if (kind == InboundActionKind.Status)
        {
            return new InboundAction(IsAdmin(command.RoleIds, config) ? InboundActionKind.Status : InboundActionKind.StatusDenied, command.UserName, null, command.UserId)
            {
                ChannelId = command.ChannelId,
            };
        }

        if (!IsBridgeChannel(command.ChannelId, config))
        {
            return new InboundAction(InboundActionKind.WrongChannel);
        }

        if (kind == InboundActionKind.ListPlayers)
        {
            return new InboundAction(kind);
        }

        var line = command.Options.GetValueOrDefault(SlashCommandDefinitions.ConsoleOption, string.Empty).Trim().TrimStart('/');
        if (line.Length == 0)
        {
            return InboundAction.Ignore;
        }

        return IsAdmin(command.RoleIds, config)
            ? new InboundAction(InboundActionKind.Console, command.UserName, line, command.UserId)
            : new InboundAction(InboundActionKind.ConsoleDenied, command.UserName, line, command.UserId);
    }

    /// <summary>True when the text starts with one of the prefixes the user chose to keep on one side.</summary>
    public static bool HasIgnoredPrefix(string text, BridgeFilters filters) =>
        filters.IgnoredPrefixes.Any(prefix => prefix.Length > 0 && text.StartsWith(prefix, StringComparison.Ordinal));

    private static bool IsBridgeChannel(ulong channelId, BridgeConfig config) =>
        ulong.TryParse(config.ChannelId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && channelId == id;

    private static bool IsAdmin(IReadOnlyList<ulong> roleIds, BridgeConfig config) =>
        ulong.TryParse(config.AdminRoleId, NumberStyles.None, CultureInfo.InvariantCulture, out var roleId) && roleIds.Contains(roleId);
}
