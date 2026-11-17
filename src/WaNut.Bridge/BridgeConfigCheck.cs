using System.Globalization;
using WaNut.Core.Bridge;
using WaNut.Core.Notifications;

namespace WaNut.Bridge;

/// <summary>Finds what in a config keeps the bridge from working, before anything starts.</summary>
public static class BridgeConfigCheck
{
    public static IReadOnlyList<string> Problems(BridgeConfig config)
    {
        var problems = new List<string>();
        if (config.SchemaVersion > BridgeConfig.CurrentSchemaVersion)
        {
            problems.Add("bridge.config.json was written by a newer WaNut; update the bridge.");
        }

        if (string.IsNullOrWhiteSpace(config.LogFile))
        {
            problems.Add("logFile is empty.");
        }

        if (config.RconPort is < 1 or > 65535)
        {
            problems.Add("rconPort is not a port number.");
        }

        if (string.IsNullOrEmpty(config.RconPassword))
        {
            problems.Add("rconPassword is empty.");
        }

        var hasBot = !string.IsNullOrWhiteSpace(config.BotToken);
        if (config.Presentation == DiscordPresentation.Webhook)
        {
            if (!DiscordWebhook.IsValidUrl(config.WebhookUrl))
            {
                problems.Add("webhookUrl is not a Discord webhook URL.");
            }
        }
        else if (!hasBot)
        {
            problems.Add("botToken is needed to post embeds or plain text.");
        }

        if (hasBot && !IsSnowflake(config.ChannelId))
        {
            problems.Add("channelId is not a Discord channel id.");
        }

        if (config.Filters.RemoteConsole && !IsSnowflake(config.AdminRoleId))
        {
            problems.Add("adminRoleId is needed for the remote console.");
        }

        return problems;
    }

    private static bool IsSnowflake(string? id) => ulong.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0;
}
