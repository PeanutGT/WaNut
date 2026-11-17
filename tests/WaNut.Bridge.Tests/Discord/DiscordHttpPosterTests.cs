using System.Net;
using System.Text.Json;
using WaNut.Bridge.Discord;

namespace WaNut.Bridge.Tests.Discord;

public sealed class DiscordHttpPosterTests
{
    [Fact]
    public async Task PostAsync_Webhook_SendsSnakeCaseJsonWithMentionsOff()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var poster = DiscordHttpPoster.ForWebhook(new HttpClient(handler), "https://discord.com/api/webhooks/1/abc");

        var result = await poster.PostAsync(new DiscordPost { Content = "hi", Username = "Steve", AvatarUrl = "https://x/a.png" }, CancellationToken.None);

        Assert.Equal(DiscordPostOutcome.Sent, result.Outcome);
        using var json = JsonDocument.Parse(handler.Body!);
        Assert.Equal("hi", json.RootElement.GetProperty("content").GetString());
        Assert.Equal("Steve", json.RootElement.GetProperty("username").GetString());
        Assert.Equal("https://x/a.png", json.RootElement.GetProperty("avatar_url").GetString());
        Assert.Equal(0, json.RootElement.GetProperty("allowed_mentions").GetProperty("parse").GetArrayLength());
        Assert.False(json.RootElement.TryGetProperty("embeds", out _));
        Assert.Null(handler.Request!.Headers.Authorization);
    }

    [Fact]
    public async Task PostAsync_Channel_UsesTheBotTokenAndChannelUrl()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var poster = DiscordHttpPoster.ForChannel(new HttpClient(handler), "tok", 123456789012345678);

        await poster.PostAsync(new DiscordPost { Embeds = [new DiscordEmbed("x", 5)] }, CancellationToken.None);

        Assert.Equal("https://discord.com/api/v10/channels/123456789012345678/messages", handler.Request!.RequestUri!.ToString());
        Assert.Equal("Bot tok", handler.Request.Headers.Authorization!.ToString());
        Assert.Contains("\"embeds\":[{\"description\":\"x\",\"color\":5}]", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostAsync_429WithJsonRetryAfter_ReturnsThatWait()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("{\"message\":\"You are being rate limited.\",\"retry_after\":1.5,\"global\":false}"),
        });
        var poster = DiscordHttpPoster.ForWebhook(new HttpClient(handler), "https://discord.com/api/webhooks/1/abc");

        var result = await poster.PostAsync(new DiscordPost { Content = "hi" }, CancellationToken.None);

        Assert.Equal(new DiscordPostResult(DiscordPostOutcome.RateLimited, TimeSpan.FromSeconds(1.5), 429), result);
    }

    [Fact]
    public void RetryWait_NoJsonBody_UsesRetryAfterHeader()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.TryAddWithoutValidation("Retry-After", "4");

        Assert.Equal(TimeSpan.FromSeconds(4), DiscordHttpPoster.RetryWait(response, "<html>", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void BucketWait_LastSlotUsed_ReturnsResetAfter()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NoContent);
        response.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", "0");
        response.Headers.TryAddWithoutValidation("X-RateLimit-Reset-After", "1.25");

        Assert.Equal(TimeSpan.FromSeconds(1.25), DiscordHttpPoster.BucketWait(response));
    }

    [Fact]
    public void BucketWait_SlotsLeft_ReturnsZero()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NoContent);
        response.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", "3");
        response.Headers.TryAddWithoutValidation("X-RateLimit-Reset-After", "1.25");

        Assert.Equal(TimeSpan.Zero, DiscordHttpPoster.BucketWait(response));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, DiscordPostOutcome.Rejected)]
    [InlineData(HttpStatusCode.Unauthorized, DiscordPostOutcome.Rejected)]
    [InlineData(HttpStatusCode.BadGateway, DiscordPostOutcome.Failed)]
    public async Task PostAsync_ErrorStatus_SaysWhetherToRetry(HttpStatusCode status, DiscordPostOutcome outcome)
    {
        var poster = DiscordHttpPoster.ForWebhook(new HttpClient(new RecordingHandler(_ => new HttpResponseMessage(status))), "https://discord.com/api/webhooks/1/abc");

        Assert.Equal(outcome, (await poster.PostAsync(new DiscordPost { Content = "x" }, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task PostAsync_NetworkDown_IsWorthRetrying()
    {
        var poster = DiscordHttpPoster.ForWebhook(new HttpClient(new RecordingHandler(_ => throw new HttpRequestException("down"))), "https://discord.com/api/webhooks/1/abc");

        Assert.Equal(DiscordPostOutcome.Failed, (await poster.PostAsync(new DiscordPost { Content = "x" }, CancellationToken.None)).Outcome);
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return answer(request);
        }
    }
}
