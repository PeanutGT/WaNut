using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace WaNut.Bridge.Tests.Stress;

/// <summary>
/// A pretend Discord webhook endpoint on a virtual clock: 5 requests per 2 second window, then 429 with
/// <c>retry_after</c>, like the real thing. Time only moves when the code under test waits, so a ten-minute flood runs
/// in milliseconds.
/// </summary>
internal sealed class FakeDiscordWebhook : HttpMessageHandler
{
    public const int RequestsPerWindow = 5;

    public static readonly TimeSpan Window = TimeSpan.FromSeconds(2);

    private TimeSpan _windowStart;
    private int _used;

    public TimeSpan Now { get; private set; }

    /// <summary>Send X-RateLimit-* headers on every answer, as Discord does.</summary>
    public bool SendBucketHeaders { get; init; } = true;

    /// <summary>While the virtual time is inside this span every request fails like a dead network.</summary>
    public (TimeSpan From, TimeSpan To)? Outage { get; set; }

    private readonly List<string> _accepted = [];

    /// <summary>Content of every accepted post, in arrival order (a copy: the outbox keeps posting while a test reads).</summary>
    public List<string> Accepted
    {
        get
        {
            lock (_accepted)
            {
                return [.. _accepted];
            }
        }
    }

    public void Advance(TimeSpan time) => Now += time;

    public int TooManyRequests { get; private set; }

    public int Requests { get; private set; }

    /// <summary>The longest accepted request body, to check the 2000 character limit.</summary>
    public int LongestContent { get; private set; }

    public async Task Delay(TimeSpan wait, CancellationToken cancellationToken)
    {
        Now += wait;
        await Task.Yield();
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests++;
        if (Outage is { } outage && Now >= outage.From && Now < outage.To)
        {
            throw new HttpRequestException("network is down");
        }

        if (Now - _windowStart >= Window)
        {
            _windowStart = Now;
            _used = 0;
        }

        if (_used >= RequestsPerWindow)
        {
            TooManyRequests++;
            var retryAfter = (_windowStart + Window - Now).TotalSeconds;
            var limited = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent(
                    $$"""{"message":"You are being rate limited.","retry_after":{{retryAfter.ToString(CultureInfo.InvariantCulture)}},"global":false}""",
                    Encoding.UTF8,
                    "application/json"),
            };
            return limited;
        }

        _used++;
        var body = await request.Content!.ReadAsStringAsync(cancellationToken);
        using var json = JsonDocument.Parse(body);
        var content = json.RootElement.TryGetProperty("content", out var c) ? c.GetString() : null;
        if (content is not null)
        {
            lock (_accepted)
            {
                _accepted.Add(content);
            }

            LongestContent = Math.Max(LongestContent, content.Length);
        }

        var ok = new HttpResponseMessage(HttpStatusCode.NoContent);
        if (SendBucketHeaders)
        {
            ok.Headers.Add("X-RateLimit-Remaining", (RequestsPerWindow - _used).ToString(CultureInfo.InvariantCulture));
            ok.Headers.Add("X-RateLimit-Reset-After", (_windowStart + Window - Now).TotalSeconds.ToString(CultureInfo.InvariantCulture));
        }

        return ok;
    }
}
