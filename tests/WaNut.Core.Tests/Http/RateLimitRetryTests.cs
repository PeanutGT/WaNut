using System.Net;
using System.Net.Http.Headers;
using WaNut.Core.Http;
using WaNut.Core.Instances;
using WaNut.Core.Mods;

namespace WaNut.Core.Tests.Http;

public sealed class RateLimitRetryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Answers 429 the given number of times, then 200 with the body.</summary>
    private sealed class BusyHandler(int busyTimes, string body, Action<HttpResponseMessage>? decorate = null) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            if (Calls <= busyTimes)
            {
                var busy = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                decorate?.Invoke(busy);
                return Task.FromResult(busy);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    [Fact]
    public void WaitTime_RetryAfterSeconds_UsesThem()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(7));

        Assert.Equal(TimeSpan.FromSeconds(7), RateLimitRetry.WaitTime(response, Now));
    }

    [Fact]
    public void WaitTime_RetryAfterDate_WaitsUntilThen()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(Now.AddSeconds(5));

        Assert.Equal(TimeSpan.FromSeconds(5), RateLimitRetry.WaitTime(response, Now));
    }

    [Fact]
    public void WaitTime_OnlyModrinthResetHeader_UsesIt()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.Add("X-Ratelimit-Reset", "12");

        Assert.Equal(TimeSpan.FromSeconds(12), RateLimitRetry.WaitTime(response, Now));
    }

    [Fact]
    public void WaitTime_NoHeaders_WaitsTwoSeconds()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);

        Assert.Equal(TimeSpan.FromSeconds(2), RateLimitRetry.WaitTime(response, Now));
    }

    [Fact]
    public void WaitTime_TooLongOrPast_IsClamped()
    {
        using var late = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        late.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromHours(1));
        using var past = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        past.Headers.RetryAfter = new RetryConditionHeaderValue(Now.AddSeconds(-30));

        Assert.Equal(RateLimitRetry.MaxWait, RateLimitRetry.WaitTime(late, Now));
        Assert.Equal(TimeSpan.Zero, RateLimitRetry.WaitTime(past, Now));
    }

    [Fact]
    public async Task ModrinthClient_RateLimitedTwice_WaitsAsAskedAndSucceeds()
    {
        var handler = new BusyHandler(2, """{"title":"Sodium"}""", r => r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(3)));
        var waits = new List<TimeSpan>();
        var client = new ModrinthClient(new HttpClient(handler), (wait, _) => { waits.Add(wait); return Task.CompletedTask; });

        var title = await client.GetProjectTitleAsync("sodium");

        Assert.Equal("Sodium", title);
        Assert.Equal(3, handler.Calls);
        Assert.Equal([TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3)], waits);
    }

    [Fact]
    public async Task ModrinthClient_StillRateLimited_GivesUpWithHttpError()
    {
        var handler = new BusyHandler(int.MaxValue, "{}");
        var client = new ModrinthClient(new HttpClient(handler), (_, _) => Task.CompletedTask);

        var e = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.SearchAsync(ModPlatform.For(ServerType.Fabric)!, "1.21.1", "sodium", 0));

        Assert.Equal(HttpStatusCode.TooManyRequests, e.StatusCode);
        Assert.Equal(RateLimitRetry.MaxAttempts, handler.Calls);
    }

    [Fact]
    public async Task ModrinthClient_RateLimitedPost_IsSentAgainWithItsBody()
    {
        var handler = new BusyHandler(1, """{"h1":{"id":"v1","project_id":"P7dR8mSH"}}""");
        var client = new ModrinthClient(new HttpClient(handler), (_, _) => Task.CompletedTask);

        var files = await client.IdentifyFilesAsync(["h1"]);

        Assert.Equal("P7dR8mSH", files["h1"]);
        Assert.Equal(2, handler.Calls);
    }
}
