using WaNut.Core.Bridge;

namespace WaNut.Bridge.Tests;

public sealed class BridgeConfigCheckTests
{
    private static readonly BridgeConfig Valid = new()
    {
        LogFile = "/srv/mc/logs/latest.log",
        RconPassword = "pw",
        WebhookUrl = "https://discord.com/api/webhooks/1/abc",
    };

    [Fact]
    public void Problems_WebhookOnly_IsFine()
    {
        Assert.Empty(BridgeConfigCheck.Problems(Valid));
    }

    [Fact]
    public void Problems_EmbedsWithoutBot_AreReported()
    {
        Assert.Single(BridgeConfigCheck.Problems(Valid with { Presentation = DiscordPresentation.Embed }));
    }

    [Fact]
    public void Problems_BotWithoutChannel_IsReported()
    {
        Assert.Single(BridgeConfigCheck.Problems(Valid with { BotToken = "tok" }));
    }

    [Fact]
    public void Problems_NotADiscordWebhook_IsReported()
    {
        Assert.Single(BridgeConfigCheck.Problems(Valid with { WebhookUrl = "https://example.com/api/webhooks/1/abc" }));
    }

    [Fact]
    public void Problems_ConsoleWithoutAdminRole_IsReported()
    {
        Assert.Single(BridgeConfigCheck.Problems(Valid with { Filters = new BridgeFilters { RemoteConsole = true } }));
    }

    [Fact]
    public void Problems_NewerSchema_IsReported()
    {
        Assert.Single(BridgeConfigCheck.Problems(Valid with { SchemaVersion = BridgeConfig.CurrentSchemaVersion + 1 }));
    }
}
