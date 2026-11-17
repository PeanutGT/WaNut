using WaNut.Bridge.Discord;
using WaNut.Bridge.Relay;
using WaNut.Core.Bridge;

namespace WaNut.Bridge.Tests.Relay;

public sealed class InboundRouterTests
{
    private const ulong Channel = 100;
    private const ulong Bot = 999;
    private const ulong AdminRole = 55;

    private static readonly BridgeConfig Config = new()
    {
        ChannelId = "100",
        AdminRoleId = "55",
        Filters = new BridgeFilters { RemoteConsole = true, IgnoredPrefixes = ["!"] },
    };

    [Fact]
    public void Route_MemberChat_IsShownInGame()
    {
        Assert.Equal(new InboundAction(InboundActionKind.Chat, "Amy", "hello"), InboundRouter.Route(Message("  hello "), Config, Bot));
    }

    [Theory]
    [InlineData(true, false, 1)]
    [InlineData(false, true, 1)]
    [InlineData(false, false, Bot)]
    public void Route_BotsWebhooksAndItself_AreIgnored(bool isBot, bool isWebhook, ulong author)
    {
        var message = Message("hello") with { IsBot = isBot, IsWebhook = isWebhook, AuthorId = author };

        Assert.Equal(InboundActionKind.Ignore, InboundRouter.Route(message, Config, Bot).Kind);
    }

    [Fact]
    public void Route_OtherChannel_IsIgnored()
    {
        Assert.Equal(InboundActionKind.Ignore, InboundRouter.Route(Message("hello") with { ChannelId = 101 }, Config, Bot).Kind);
    }

    [Fact]
    public void Route_IgnoredPrefix_StaysInDiscord()
    {
        Assert.Equal(InboundActionKind.Ignore, InboundRouter.Route(Message("!just us"), Config, Bot).Kind);
    }

    [Fact]
    public void Route_ChatOff_IsIgnored()
    {
        var config = Config with { Filters = new BridgeFilters { Chat = false } };

        Assert.Equal(InboundActionKind.Ignore, InboundRouter.Route(Message("hello"), config, Bot).Kind);
    }

    [Theory]
    [InlineData("/list")]
    [InlineData("/LIST ")]
    public void Route_List_AsksForPlayers(string text)
    {
        Assert.Equal(InboundActionKind.ListPlayers, InboundRouter.Route(Message(text), Config, Bot).Kind);
    }

    [Fact]
    public void Route_ListOff_IsIgnored()
    {
        var config = Config with { Filters = new BridgeFilters { ListCommand = false } };

        Assert.Equal(InboundActionKind.Ignore, InboundRouter.Route(Message("/list"), config, Bot).Kind);
    }

    [Fact]
    public void Route_WordStartingWithList_IsChat()
    {
        Assert.Equal(InboundActionKind.Chat, InboundRouter.Route(Message("/listen to this"), Config, Bot).Kind);
    }

    [Fact]
    public void Route_CmdFromAdmin_RunsTheCommandWithoutSlash()
    {
        var message = Message("/cmd /time set day") with { RoleIds = [1, AdminRole] };

        Assert.Equal(new InboundAction(InboundActionKind.Console, "Amy", "time set day"), InboundRouter.Route(message, Config, Bot));
    }

    [Fact]
    public void Route_CmdFromMember_IsDenied()
    {
        Assert.Equal(InboundActionKind.ConsoleDenied, InboundRouter.Route(Message("/cmd op Amy"), Config, Bot).Kind);
    }

    [Fact]
    public void Route_CmdWhenConsoleOff_IsIgnoredEvenForAdmins()
    {
        var config = Config with { Filters = new BridgeFilters() };
        var message = Message("/cmd op Amy") with { RoleIds = [AdminRole] };

        Assert.Equal(InboundActionKind.Ignore, InboundRouter.Route(message, config, Bot).Kind);
    }

    private static InboundMessage Message(string content) => new(Channel, 1, "Amy", false, false, [], content);
}
