using System.Net.Http.Json;
using System.Text;

namespace WaNut.Core.Notifications;

/// <summary>
/// Posts short messages to a Discord channel through a webhook URL. The URL works like a password (anyone with it
/// can post), so it stays in config.json on this PC and is never logged.
/// </summary>
public sealed class DiscordWebhook(HttpClient http)
{
    private static readonly HashSet<string> Hosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "discord.com", "discordapp.com", "ptb.discord.com", "canary.discord.com",
    };

    /// <summary>True for an https Discord webhook URL, so WaNut never posts the server's events anywhere else.</summary>
    public static bool IsValidUrl(string? url) =>
        Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.IsDefaultPort
        && string.IsNullOrEmpty(uri.UserInfo)
        && Hosts.Contains(uri.Host)
        && uri.AbsolutePath.StartsWith("/api/webhooks/", StringComparison.Ordinal);

    /// <summary>Escapes Discord's markdown, so a player called "__x__" shows as typed.</summary>
    public static string Escape(string text)
    {
        var escaped = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is '\\' or '*' or '_' or '~' or '`' or '|' or '>' or '#' or '[' or ']' or '(' or ')')
            {
                escaped.Append('\\');
            }

            escaped.Append(c);
        }

        return escaped.ToString();
    }

    /// <summary>Posts <paramref name="message"/>. Mentions are switched off, so text like "@everyone" pings nobody.</summary>
    public async Task SendAsync(string url, string message, CancellationToken cancellationToken = default)
    {
        if (!IsValidUrl(url))
        {
            throw new ArgumentException("Not a Discord webhook URL.", nameof(url));
        }

        var body = new { content = message.Length > 2000 ? message[..2000] : message, allowed_mentions = new { parse = Array.Empty<string>() } };
        using var response = await http.PostAsJsonAsync(url.Trim(), body, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }
}
