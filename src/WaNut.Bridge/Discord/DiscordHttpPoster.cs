using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using WaNut.Core.Http;

namespace WaNut.Bridge.Discord;

/// <summary>
/// Sends posts to a webhook or, with the bot token, to a channel. Reads Discord's rate-limit answers: the wait a 429
/// asks for, and the bucket headers on a success so the next post waits instead of hitting the limit.
/// </summary>
public sealed class DiscordHttpPoster(HttpClient http, Uri endpoint, string? botToken = null) : IDiscordPoster
{
    public const string ApiBase = "https://discord.com/api/v10/";

    public static DiscordHttpPoster ForWebhook(HttpClient http, string webhookUrl) => new(http, new Uri(webhookUrl.Trim()));

    public static DiscordHttpPoster ForChannel(HttpClient http, string botToken, ulong channelId) =>
        new(http, new Uri($"{ApiBase}channels/{channelId.ToString(CultureInfo.InvariantCulture)}/messages"), botToken);

    public async Task<DiscordPostResult> PostAsync(DiscordPost post, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(post, DiscordJsonContext.Default.DiscordPost), Encoding.UTF8, "application/json"),
        };
        if (botToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bot", botToken);
        }

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return new DiscordPostResult(DiscordPostOutcome.Failed);
        }

        using (response)
        {
            var status = (int)response.StatusCode;
            if (response.IsSuccessStatusCode)
            {
                return new DiscordPostResult(DiscordPostOutcome.Sent, BucketWait(response), status);
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return new DiscordPostResult(DiscordPostOutcome.RateLimited, RetryWait(response, body, DateTimeOffset.UtcNow), status);
            }

            return new DiscordPostResult(status >= 500 ? DiscordPostOutcome.Failed : DiscordPostOutcome.Rejected, StatusCode: status);
        }
    }

    /// <summary>
    /// Discord's JSON <c>retry_after</c> (seconds, with fractions), else the Retry-After header; never more than
    /// <see cref="RateLimitRetry.MaxWait"/>.
    /// </summary>
    public static TimeSpan RetryWait(HttpResponseMessage response, string body, DateTimeOffset now)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.ValueKind == JsonValueKind.Object
                && json.RootElement.TryGetProperty("retry_after", out var retryAfter)
                && retryAfter.TryGetDouble(out var seconds)
                && seconds >= 0)
            {
                var wait = TimeSpan.FromSeconds(seconds);
                return wait > RateLimitRetry.MaxWait ? RateLimitRetry.MaxWait : wait;
            }
        }
        catch (JsonException)
        {
            // Not JSON (a proxy's page): use the header.
        }

        return RateLimitRetry.WaitTime(response, now);
    }

    /// <summary>How long until the bucket refills when this request used its last slot.</summary>
    public static TimeSpan BucketWait(HttpResponseMessage response)
    {
        if (Header(response, "X-RateLimit-Remaining") == "0"
            && double.TryParse(Header(response, "X-RateLimit-Reset-After"), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            && seconds > 0)
        {
            var wait = TimeSpan.FromSeconds(seconds);
            return wait > RateLimitRetry.MaxWait ? RateLimitRetry.MaxWait : wait;
        }

        return TimeSpan.Zero;
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
}
