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

    [Fact]
    public void RouteCommand_List_AsksForPlayers()
    {
        Assert.Equal(InboundActionKind.ListPlayers, InboundRouter.Route(Command("list"), Config).Kind);
    }

    [Fact]
    public void RouteCommand_ListOff_IsIgnored()
    {
        var config = Config with { Filters = new BridgeFilters { ListCommand = false } };

        Assert.Equal(InboundActionKind.Ignore, InboundRouter.Route(Command("list"), config).Kind);
    }

    [Fact]
    public void RouteCommand_OtherChannel_SaysWhereToUseIt()
    {
        Assert.Equal(InboundActionKind.WrongChannel, InboundRouter.Route(Command("list") with { ChannelId = 101 }, Config).Kind);
    }

    [Fact]
    public void RouteCommand_CmdFromAdmin_RunsTheCommandWithoutSlash()
    {
        var command = Command("cmd", "/time set day") with { RoleIds = [1, AdminRole] };

        Assert.Equal(new InboundAction(InboundActionKind.Console, "Amy", "time set day"), InboundRouter.Route(command, Config));
    }

    [Fact]
    public void RouteCommand_CmdFromMember_IsDenied()
    {
        Assert.Equal(InboundActionKind.ConsoleDenied, InboundRouter.Route(Command("cmd", "op Amy"), Config).Kind);
    }

    [Fact]
    public void RouteCommand_CmdWhenConsoleOff_IsIgnoredEvenForAdmins()
    {
        var config = Config with { Filters = new BridgeFilters() };
        var command = Command("cmd", "op Amy") with { RoleIds = [AdminRole] };

        Assert.Equal(InboundActionKind.Ignore, InboundRouter.Route(command, config).Kind);
    }

    [Fact]
    public void Route_TextThatLooksLikeACommand_IsJustChat()
    {
        Assert.Equal(InboundActionKind.Chat, InboundRouter.Route(Message("/list"), Config, Bot).Kind);
    }

    private static SlashCommand Command(string name, string? line = null) =>
        new("1", "tok", "app", Channel, 1, "Amy", [], name, line is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["command"] = line });

    private static InboundMessage Message(string content) => new(Channel, 1, "Amy", false, false, [], content);
}
