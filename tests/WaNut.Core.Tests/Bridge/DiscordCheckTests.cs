using System.Net;
using WaNut.Core.Bridge;

namespace WaNut.Core.Tests.Bridge;

public sealed class DiscordCheckTests
{
    private const string Webhook = "https://discord.com/api/webhooks/1/abc";
    private const string Api = "https://discord.com/api/v10/";

    // Made-up, but shaped like a real one: base64 user id, a short time part, a long signature.
    private static readonly string Token = string.Join('.', "MTIzNDU2Nzg5MDEyMzQ1Njc4", "GabcDE", "abcdefghijklmnopqrstuvwxyz0123456789ABCD");

    private static readonly BridgeConfig BotConfig = new()
    {
        ServerId = "s",
        BotToken = Token,
        ChannelId = "100",
        Presentation = DiscordPresentation.Embed,
    };

    [Fact]
    public async Task CheckAsync_WorkingBot_PassesWithNoWarnings()
    {
        var warnings = await Check(Discord(), BotConfig);

        Assert.Empty(warnings);
    }

    [Fact]
    public async Task CheckAsync_TokenRefused_IsBotTokenRejected()
    {
        var discord = Discord();
        discord[Api + "users/@me"] = (HttpStatusCode.Unauthorized, "{}");

        var error = await Assert.ThrowsAsync<BridgeDeployException>(() => Check(discord, BotConfig));

        Assert.Equal(BridgeDeployError.BotTokenRejected, error.Error);
        Assert.Equal(BridgeDeployStep.Validate, error.Step);
        Assert.Equal("HTTP 401", error.Detail);
        Assert.DoesNotContain(Token, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckAsync_SendsTheTokenAsBotAuthorization()
    {
        var seen = new List<HttpRequestMessage>();

        await Check(Discord(), BotConfig, seen);

        Assert.All(seen.Where(r => r.RequestUri!.AbsoluteUri.StartsWith(Api, StringComparison.Ordinal)), r =>
        {
            Assert.Equal("Bot", r.Headers.Authorization!.Scheme);
            Assert.Equal(Token, r.Headers.Authorization.Parameter);
        });
    }

    [Theory]
    [InlineData("  {token}\r\n")]
    [InlineData("Bot {token}")]
    [InlineData("\"{token}\"")]
    [InlineData("bot {token}\n")]
    [InlineData("{half1}\r\n{half2}")]
    public async Task CheckAsync_PastedExtras_AreStrippedBeforeSending(string pasted)
    {
        var text = pasted.Replace("{token}", Token).Replace("{half1}", Token[..30]).Replace("{half2}", Token[30..]);
        var seen = new List<HttpRequestMessage>();

        await Check(Discord(), BotConfig with { BotToken = text }, seen);

        Assert.Equal(Token, seen[0].Headers.Authorization!.Parameter);
    }

    [Theory]
    [InlineData("123456789012345678", BridgeDeployError.BotTokenIsApplicationId)]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", BridgeDeployError.BotTokenIsPublicKey)]
    [InlineData("abcDEF0123456789abcDEF0123456789", BridgeDeployError.BotTokenIsClientSecret)]
    [InlineData("MTIzNDU2.Gabc", BridgeDeployError.BotTokenMalformed)]
    [InlineData("MTIzNDU2.GabcDE.ｆｕｌｌｗｉｄｔｈ", BridgeDeployError.BotTokenMalformed)]
    public async Task CheckAsync_WrongThingPasted_SaysWhichAndSendsNothing(string pasted, BridgeDeployError expected)
    {
        var seen = new List<HttpRequestMessage>();

        var error = await Assert.ThrowsAsync<BridgeDeployException>(() => Check(Discord(), BotConfig with { BotToken = pasted }, seen));

        Assert.Equal(expected, error.Error);
        Assert.Empty(seen);
    }

    [Fact]
    public async Task CheckAsync_ServerError_ShowsTheStatusButNoAddress()
    {
        var discord = Discord();
        discord[Api + "users/@me"] = (HttpStatusCode.BadGateway, "{}");

        var error = await Assert.ThrowsAsync<BridgeDeployException>(() => Check(discord, BotConfig));

        Assert.Equal(BridgeDeployError.DiscordUnreachable, error.Error);
        Assert.Equal("HTTP 502", error.Detail);
    }

    [Fact]
    public async Task CheckAsync_UnknownChannel_IsChannelNotFound()
    {
        var discord = Discord();
        discord[Api + "channels/100"] = (HttpStatusCode.NotFound, "{}");

        var error = await Assert.ThrowsAsync<BridgeDeployException>(() => Check(discord, BotConfig));

        Assert.Equal(BridgeDeployError.ChannelNotFound, error.Error);
    }

    [Fact]
    public async Task CheckAsync_ChannelHiddenFromBot_IsChannelNoAccess()
    {
        var discord = Discord(overwrites: """[{"id":"7","type":0,"allow":"0","deny":"1024"}]""");

        var error = await Assert.ThrowsAsync<BridgeDeployException>(() => Check(discord, BotConfig));

        Assert.Equal(BridgeDeployError.ChannelNoAccess, error.Error);
    }

    [Fact]
    public async Task CheckAsync_NoEmbedLinks_ListsTheMissingPermission()
    {
        var discord = Discord(everyone: "3072");

        var error = await Assert.ThrowsAsync<BridgeDeployException>(() => Check(discord, BotConfig));

        Assert.Equal(BridgeDeployError.MissingPermissions, error.Error);
        Assert.Equal("Embed Links", error.Detail);
    }

    [Fact]
    public async Task CheckAsync_MessageContentIntentOff_Warns()
    {
        var discord = Discord(appFlags: 0);

        var warnings = await Check(discord, BotConfig);

        Assert.Equal([BridgeDeployWarning.MessageContentIntentOff], warnings);
    }

    [Fact]
    public async Task CheckAsync_DeletedWebhook_IsWebhookInvalid()
    {
        var discord = new Dictionary<string, (HttpStatusCode, string)> { [Webhook] = (HttpStatusCode.NotFound, "{}") };

        var error = await Assert.ThrowsAsync<BridgeDeployException>(() => Check(discord, new BridgeConfig { WebhookUrl = Webhook }));

        Assert.Equal(BridgeDeployError.WebhookInvalid, error.Error);
    }

    [Fact]
    public async Task CheckAsync_WebhookOnly_ChecksOnlyTheWebhook()
    {
        var discord = new Dictionary<string, (HttpStatusCode, string)> { [Webhook] = (HttpStatusCode.OK, "{}") };

        Assert.Empty(await Check(discord, new BridgeConfig { WebhookUrl = Webhook }));
    }

    private static Dictionary<string, (HttpStatusCode, string)> Discord(string everyone = "19456", string overwrites = "[]", long appFlags = 1L << 19) => new()
    {
        [Api + "users/@me"] = (HttpStatusCode.OK, """{"id":"5"}"""),
        [Api + "applications/@me"] = (HttpStatusCode.OK, $$"""{"flags":{{appFlags}}}"""),
        [Api + "channels/100"] = (HttpStatusCode.OK, $$"""{"id":"100","guild_id":"7","permission_overwrites":{{overwrites}}}"""),
        [Api + "guilds/7"] = (HttpStatusCode.OK, $$"""{"owner_id":"8","roles":[{"id":"7","permissions":"{{everyone}}"}]}"""),
        [Api + "guilds/7/members/5"] = (HttpStatusCode.OK, """{"roles":[]}"""),
    };

    private static async Task<IReadOnlyList<BridgeDeployWarning>> Check(
        Dictionary<string, (HttpStatusCode Status, string Body)> answers, BridgeConfig config, List<HttpRequestMessage>? seen = null)
    {
        using var http = new HttpClient(new Handler(answers, seen));
        return await new DiscordCheck(http, (_, _) => Task.CompletedTask).CheckAsync(config);
    }

    private sealed class Handler(Dictionary<string, (HttpStatusCode Status, string Body)> answers, List<HttpRequestMessage>? seen) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            seen?.Add(request);
            var (status, body) = answers.TryGetValue(request.RequestUri!.AbsoluteUri, out var answer) ? answer : (HttpStatusCode.NotFound, "{}");
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
}
