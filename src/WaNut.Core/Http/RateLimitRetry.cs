using System.Globalization;
using System.Net;

namespace WaNut.Core.Http;

/// <summary>
/// Sends a request again when the server answers 429 Too Many Requests, after the wait it asks for. Modrinth allows
/// 300 requests a minute per IP; a large modpack or many mods at once can go over that.
/// </summary>
public static class RateLimitRetry
{
    public const int MaxAttempts = 4;

    /// <summary>The longest wait between attempts, whatever the server asks for, so a window never hangs.</summary>
    public static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan DefaultWait = TimeSpan.FromSeconds(2);

    /// <param name="createRequest">Makes a fresh request for each attempt; a request can only be sent once.</param>
    /// <param name="delay">Waits between attempts; tests pass one that returns at once.</param>
    /// <returns>The first answer that is not 429, or the last 429 after <see cref="MaxAttempts"/> tries.</returns>
    public static async Task<HttpResponseMessage> SendAsync(
        HttpClient http,
        Func<HttpRequestMessage> createRequest,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        CancellationToken cancellationToken = default)
    {
        delay ??= Task.Delay;
        for (var attempt = 1; ; attempt++)
        {
            using var request = createRequest();
            var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.TooManyRequests || attempt == MaxAttempts)
            {
                return response;
            }

            var wait = WaitTime(response, DateTimeOffset.UtcNow);
            response.Dispose();
            await delay(wait, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Retry-After (seconds or a date), else Modrinth's X-Ratelimit-Reset (seconds until the window resets), else
    /// two seconds; never less than zero or more than <see cref="MaxWait"/>.
    /// </summary>
    public static TimeSpan WaitTime(HttpResponseMessage response, DateTimeOffset now)
    {
        var retryAfter = response.Headers.RetryAfter;
        var wait = retryAfter?.Delta ?? (retryAfter?.Date - now);
        if (wait is null
            && response.Headers.TryGetValues("X-Ratelimit-Reset", out var values)
            && int.TryParse(values.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
        {
            wait = TimeSpan.FromSeconds(seconds);
        }

        var result = wait ?? DefaultWait;
        return result < TimeSpan.Zero ? TimeSpan.Zero : result > MaxWait ? MaxWait : result;
    }
}
