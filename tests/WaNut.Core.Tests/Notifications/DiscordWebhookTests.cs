using System.Net;
using WaNut.Core.Notifications;

namespace WaNut.Core.Tests.Notifications;

public class DiscordWebhookTests
{
    private const string Url = "https://discord.com/api/webhooks/123/abc";

    [Theory]
    [InlineData(Url, true)]
    [InlineData("https://discordapp.com/api/webhooks/123/abc", true)]
    [InlineData("https://canary.discord.com/api/webhooks/123/abc", true)]
    [InlineData("http://discord.com/api/webhooks/123/abc", false)]
    [InlineData("https://discord.com.evil.example/api/webhooks/123/abc", false)]
    [InlineData("https://discord.com/channels/123", false)]
    [InlineData("https://discord.com:8443/api/webhooks/123/abc", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidUrl_Various_OnlyDiscordWebhooks(string? url, bool valid) => Assert.Equal(valid, DiscordWebhook.IsValidUrl(url));

    [Fact]
    public void Escape_MarkdownCharacters_AreEscaped() => Assert.Equal(@"\_\_Steve\_\_ \*x\*", DiscordWebhook.Escape("__Steve__ *x*"));

    [Fact]
    public async Task SendAsync_Message_PostsContentWithMentionsOff()
    {
        var handler = new CapturingHandler();
        var webhook = new DiscordWebhook(new HttpClient(handler));

        await webhook.SendAsync(Url, "Survival is online @everyone");

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal(Url, handler.Uri!.AbsoluteUri);
        Assert.Contains("\"content\":\"Survival is online @everyone\"", handler.Body);
        Assert.Contains("\"allowed_mentions\":{\"parse\":[]}", handler.Body);
    }

    [Fact]
    public async Task SendAsync_NotAWebhook_Throws()
    {
        var webhook = new DiscordWebhook(new HttpClient(new CapturingHandler()));

        await Assert.ThrowsAsync<ArgumentException>(() => webhook.SendAsync("https://example.com/api/webhooks/1/a", "hi"));
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }

        public Uri? Uri { get; private set; }

        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            Uri = request.RequestUri;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }
}

public class DiscordSettingsTests
{
    [Fact]
    public void IsOn_NoUrl_IsOffForEverything() => Assert.False(new DiscordSettings().IsOn(DiscordEvent.Online));

    [Fact]
    public void IsOn_Defaults_OnlineStoppedCrashedOnPlayersOff()
    {
        var settings = new DiscordSettings { WebhookUrl = "https://discord.com/api/webhooks/1/a" };

        Assert.True(settings.IsOn(DiscordEvent.Online));
        Assert.True(settings.IsOn(DiscordEvent.Crashed));
        Assert.False(settings.IsOn(DiscordEvent.PlayerJoined));
    }
}
