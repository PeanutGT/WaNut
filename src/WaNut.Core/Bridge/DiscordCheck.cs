using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using WaNut.Core.Http;
using WaNut.Core.Notifications;

namespace WaNut.Core.Bridge;

/// <summary>
/// Step 1 of a deployment, run from this PC before anything is changed: the bot token works, the bot can see the
/// channel and has the permissions the chosen presentation needs, and the webhook still exists.
/// </summary>
public sealed class DiscordCheck(HttpClient http, Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    public static readonly Uri ApiBase = new("https://discord.com/api/v10/");

    /// <summary>The application flags that let a bot read message text (verified apps, and apps under 100 servers).</summary>
    public const long MessageContentFlags = (1L << 18) | (1L << 19);

    /// <returns>Warnings for things that work but not fully, such as a bot that can't read message text.</returns>
    /// <exception cref="BridgeDeployException">The configuration can't work; its error says why.</exception>
    public async Task<IReadOnlyList<BridgeDeployWarning>> CheckAsync(BridgeConfig config, CancellationToken cancellationToken = default)
    {
        var warnings = new List<BridgeDeployWarning>();
        try
        {
            if (config.Presentation == DiscordPresentation.Webhook)
            {
                await CheckWebhookAsync(config.WebhookUrl, cancellationToken).ConfigureAwait(false);
            }

            if (!string.IsNullOrWhiteSpace(config.BotToken))
            {
                await CheckBotAsync(config, warnings, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException
            || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // Only the HTTP status goes into the detail: a network error's own text can carry the webhook address.
            throw Fail(BridgeDeployError.DiscordUnreachable, ex is HttpRequestException { StatusCode: { } code } ? $"HTTP {(int)code}" : null, ex);
        }

        return warnings;
    }

    private async Task CheckWebhookAsync(string? url, CancellationToken cancellationToken)
    {
        if (!DiscordWebhook.IsValidUrl(url))
        {
            throw Fail(BridgeDeployError.WebhookInvalid);
        }

        using var response = await SendAsync(new Uri(url!.Trim()), null, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw Fail(BridgeDeployError.WebhookInvalid);
        }

        response.EnsureSuccessStatusCode();
    }

    private async Task CheckBotAsync(BridgeConfig config, List<BridgeDeployWarning> warnings, CancellationToken cancellationToken)
    {
        var token = DiscordTokenFormat.Normalize(config.BotToken);
        var mistake = DiscordTokenFormat.Classify(token) switch
        {
            DiscordTokenShape.ApplicationId => BridgeDeployError.BotTokenIsApplicationId,
            DiscordTokenShape.PublicKey => BridgeDeployError.BotTokenIsPublicKey,
            DiscordTokenShape.ClientSecret => BridgeDeployError.BotTokenIsClientSecret,
            DiscordTokenShape.Malformed => BridgeDeployError.BotTokenMalformed,
            _ => (BridgeDeployError?)null,
        };
        if (mistake is { } wrong)
        {
            throw Fail(wrong);
        }

        using var me = await GetJsonAsync("users/@me", token, _ => BridgeDeployError.BotTokenRejected, cancellationToken).ConfigureAwait(false);
        var botId = me!.RootElement.GetProperty("id").GetString()!;

        using (var app = await GetJsonAsync("applications/@me", token, _ => null, cancellationToken).ConfigureAwait(false))
        {
            if (config.Filters.Chat && app is not null && app.RootElement.TryGetProperty("flags", out var flags)
                && (flags.GetInt64() & MessageContentFlags) == 0)
            {
                warnings.Add(BridgeDeployWarning.MessageContentIntentOff);
            }
        }

        using var channel = await GetJsonAsync(
            $"channels/{Uri.EscapeDataString(config.ChannelId ?? string.Empty)}",
            token,
            status => status == HttpStatusCode.Forbidden ? BridgeDeployError.ChannelNoAccess : BridgeDeployError.ChannelNotFound,
            cancellationToken).ConfigureAwait(false);
        if (!channel!.RootElement.TryGetProperty("guild_id", out var guildIdElement) || guildIdElement.GetString() is not { } guildId)
        {
            throw Fail(BridgeDeployError.ChannelNotFound, "Not a server channel.");
        }

        using var guild = await GetJsonAsync($"guilds/{guildId}", token, _ => BridgeDeployError.ChannelNoAccess, cancellationToken).ConfigureAwait(false);
        using var member = await GetJsonAsync($"guilds/{guildId}/members/{botId}", token, _ => BridgeDeployError.ChannelNoAccess, cancellationToken).ConfigureAwait(false);

        var permissions = DiscordPermissions.InChannel(
            botId,
            guildId,
            guild!.RootElement.GetProperty("owner_id").GetString(),
            ReadRoles(guild.RootElement.GetProperty("roles")),
            member!.RootElement.GetProperty("roles").EnumerateArray().Select(r => r.GetString()!).ToList(),
            ReadOverwrites(channel.RootElement));

        var missing = DiscordPermissions.Missing(permissions, DiscordPermissions.Needed(config));
        if ((permissions & DiscordPermissions.ViewChannel) == 0)
        {
            throw Fail(BridgeDeployError.ChannelNoAccess);
        }

        if (missing.Count > 0)
        {
            throw Fail(BridgeDeployError.MissingPermissions, string.Join(", ", missing));
        }
    }

    private static Dictionary<string, long> ReadRoles(JsonElement roles) =>
        roles.EnumerateArray().ToDictionary(r => r.GetProperty("id").GetString()!, r => ReadBits(r.GetProperty("permissions")));

    private static List<DiscordOverwrite> ReadOverwrites(JsonElement channel) =>
        channel.TryGetProperty("permission_overwrites", out var list)
            ? list.EnumerateArray().Select(o => new DiscordOverwrite(
                o.GetProperty("id").GetString()!,
                o.GetProperty("type").GetInt32() == 1,
                ReadBits(o.GetProperty("allow")),
                ReadBits(o.GetProperty("deny")))).ToList()
            : [];

    // Discord sends permission bits as a decimal string, since they outgrew a JSON number long ago.
    private static long ReadBits(JsonElement value) => value.ValueKind == JsonValueKind.String
        ? long.Parse(value.GetString()!, NumberStyles.None, CultureInfo.InvariantCulture)
        : value.GetInt64();

    /// <param name="refused">The error for a 401 / 403 / 404 answer; null to return null instead.</param>
    private async Task<JsonDocument?> GetJsonAsync(string path, string token, Func<HttpStatusCode, BridgeDeployError?> refused, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(new Uri(ApiBase, path), token, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            // A token Discord refuses gets 401 everywhere, whatever was asked.
            var error = response.StatusCode == HttpStatusCode.Unauthorized ? BridgeDeployError.BotTokenRejected : refused(response.StatusCode);
            return error is { } e ? throw Fail(e, $"HTTP {(int)response.StatusCode}") : null;
        }

        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private Task<HttpResponseMessage> SendAsync(Uri url, string? token, CancellationToken cancellationToken) =>
        RateLimitRetry.SendAsync(
            http,
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (token is not null)
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bot", token);
                }

                return request;
            },
            delay,
            cancellationToken);

    private static BridgeDeployException Fail(BridgeDeployError error, string? detail = null, Exception? inner = null) =>
        new(BridgeDeployStep.Validate, error, detail, inner);
}
