using System.Net.Http;
using WaNut.App.ViewModels;
using WaNut.Core.Notifications;

namespace WaNut.App.Services;

/// <summary>Posts server events to the Discord channel set in App settings. A failed post never gets in the way.</summary>
public sealed class DiscordNotifier(DiscordWebhook webhook, AppSettingsViewModel app)
{
    public void Notify(DiscordEvent kind, string message)
    {
        var settings = app.Discord;
        if (settings.IsOn(kind))
        {
            _ = SendQuietlyAsync(settings.WebhookUrl!, message);
        }
    }

    private async Task SendQuietlyAsync(string url, string message)
    {
        try
        {
            await webhook.SendAsync(url, message);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or ArgumentException)
        {
            // Discord down, rate limited or the webhook deleted: the server itself is fine. "Send test" shows why.
        }
    }
}
