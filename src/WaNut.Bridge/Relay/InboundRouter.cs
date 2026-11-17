using System.Globalization;
using WaNut.Bridge.Discord;
using WaNut.Core.Bridge;

namespace WaNut.Bridge.Relay;

/// <summary>Decides what a Discord message in the bridge channel should do in game.</summary>
public static class InboundRouter
{
    public const string ListCommand = "/list";

    public const string ConsoleCommand = "/cmd";

    /// <param name="botUserId">The bridge bot's own id.</param>
    public static InboundAction Route(InboundMessage message, BridgeConfig config, ulong botUserId)
    {
        // Loop guard: the bridge's own posts (bot or webhook) and every other bot or webhook never go back into the game.
        if (!ulong.TryParse(config.ChannelId, NumberStyles.None, CultureInfo.InvariantCulture, out var channelId)
            || message.ChannelId != channelId
            || message.IsBot
            || message.IsWebhook
            || message.AuthorId == botUserId)
        {
            return InboundAction.Ignore;
        }

        var text = message.Content.Trim();
        if (text.Length == 0)
        {
            return InboundAction.Ignore;
        }

        if (IsCommand(text, ListCommand))
        {
            return config.Filters.ListCommand ? new InboundAction(InboundActionKind.ListPlayers) : InboundAction.Ignore;
        }

        if (IsCommand(text, ConsoleCommand))
        {
            var command = text[ConsoleCommand.Length..].Trim().TrimStart('/');
            if (!config.Filters.RemoteConsole || command.Length == 0)
            {
                return InboundAction.Ignore;
            }

            return IsAdmin(message, config)
                ? new InboundAction(InboundActionKind.Console, message.AuthorName, command)
                : new InboundAction(InboundActionKind.ConsoleDenied);
        }

        if (!config.Filters.Chat || HasIgnoredPrefix(text, config.Filters))
        {
            return InboundAction.Ignore;
        }

        return new InboundAction(InboundActionKind.Chat, message.AuthorName, text);
    }

    /// <summary>True when the text starts with one of the prefixes the user chose to keep on one side.</summary>
    public static bool HasIgnoredPrefix(string text, BridgeFilters filters) =>
        filters.IgnoredPrefixes.Any(prefix => prefix.Length > 0 && text.StartsWith(prefix, StringComparison.Ordinal));

    private static bool IsCommand(string text, string command) =>
        text.StartsWith(command, StringComparison.OrdinalIgnoreCase) && (text.Length == command.Length || char.IsWhiteSpace(text[command.Length]));

    private static bool IsAdmin(InboundMessage message, BridgeConfig config) =>
        ulong.TryParse(config.AdminRoleId, NumberStyles.None, CultureInfo.InvariantCulture, out var roleId) && message.RoleIds.Contains(roleId);
}
