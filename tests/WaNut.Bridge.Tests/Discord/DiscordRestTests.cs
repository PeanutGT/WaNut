using System.Net;
using WaNut.Bridge.Discord;

namespace WaNut.Bridge.Tests.Discord;

public sealed class DiscordRestTests
{
    private static readonly SlashCommand Command = new("11", "tok", "777", 100, 1, "Amy", [], "cmd", new Dictionary<string, string>());

    [Fact]
    public async Task DeferReplyAsync_Ephemeral_PostsTypeFiveWithFlagWithoutBotToken()
    {
        var handler = new Handler(HttpStatusCode.NoContent);

        await new DiscordRest(new HttpClient(handler), "bot").DeferReplyAsync(Command, ephemeral: true, CancellationToken.None);

        Assert.Equal("https://discord.com/api/v10/interactions/11/tok/callback", handler.Uri!.ToString());
        Assert.Equal("""{"type":5,"data":{"flags":64}}""", handler.Body);
        Assert.False(handler.HadAuthorization);
    }

    [Fact]
    public async Task EditReplyAsync_PatchesTheOriginalAnswerWithMentionsOff()
    {
        var handler = new Handler(HttpStatusCode.OK);

        await new DiscordRest(new HttpClient(handler), "bot").EditReplyAsync(Command, "done", CancellationToken.None);

        Assert.Equal(HttpMethod.Patch, handler.Method);
        Assert.Equal("https://discord.com/api/v10/webhooks/777/tok/messages/@original", handler.Uri!.ToString());
        Assert.Equal("""{"content":"done","allowed_mentions":{"parse":[]}}""", handler.Body);
    }

    [Fact]
    public async Task RegisterCommandsAsync_Refused_ThrowsWithoutTheToken()
    {
        var rest = new DiscordRest(new HttpClient(new Handler(HttpStatusCode.Forbidden)), "bot");

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => rest.RegisterCommandsAsync("777", "5", "[]", CancellationToken.None));

        Assert.Contains("403", error.Message, StringComparison.Ordinal);
    }

    private sealed class Handler(HttpStatusCode status) : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }

        public HttpMethod? Method { get; private set; }

        public string? Body { get; private set; }

        public bool HadAuthorization { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            (Uri, Method, HadAuthorization) = (request.RequestUri, request.Method, request.Headers.Authorization is not null);
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status);
        }
    }
}
