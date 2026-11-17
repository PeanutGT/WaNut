using WaNut.Bridge.Discord;
using WaNut.Bridge.GameLog;
using WaNut.Bridge.Relay;
using WaNut.Core.Bridge;

namespace WaNut.Bridge.Tests.Relay;

public sealed class BridgeRelayTests
{
    private const string Prefix = "[12:00:00] [Server thread/INFO]: ";

    private readonly List<OutgoingMessage> _posted = [];

    [Fact]
    public void OnLogLine_ChatWithIgnoredPrefix_IsNotPosted()
    {
        var relay = NewRelay(new BridgeConfig { Filters = new BridgeFilters { IgnoredPrefixes = ["#"] } });

        relay.OnLogLine(Prefix + "<Steve> #note to self");
        relay.OnLogLine(Prefix + "<Steve> hello");

        Assert.Equal(["hello"], _posted.Select(m => m.Text));
    }

    [Fact]
    public void OnLogLine_FilterOff_DropsThatKind()
    {
        var relay = NewRelay(new BridgeConfig { Filters = new BridgeFilters { JoinLeave = false, ServerState = false } });

        relay.OnLogLine(Prefix + "Steve joined the game");
        relay.OnLogLine(Prefix + "Stopping server");

        Assert.Empty(_posted);
    }

    [Fact]
    public async Task OnLogLine_DeathOfSomeoneNotOnline_IsNotPosted()
    {
        var relay = NewRelay(new BridgeConfig(), new FakeRcon(_ => "There are 1 of a max of 20 players online: Steve"));
        await relay.RefreshPlayersAsync(CancellationToken.None);

        relay.OnLogLine(Prefix + "Server was unable to bind port");
        relay.OnLogLine(Prefix + "Steve was slain by Zombie");

        Assert.Equal(["**Steve was slain by Zombie**"], _posted.Select(m => m.Text));
    }

    [Fact]
    public async Task RefreshPlayersAsync_NamesNotAllReadable_KeepsLettingDeathsThrough()
    {
        var relay = NewRelay(new BridgeConfig(), new FakeRcon(_ => "There are 1 of a max of 20 players online: [Admin] Steve"));
        await relay.RefreshPlayersAsync(CancellationToken.None);

        relay.OnLogLine(Prefix + "Steve drowned");

        Assert.Single(_posted);
    }

    [Fact]
    public async Task OnDiscordMessageAsync_Chat_SendsTellraw()
    {
        var rcon = new FakeRcon(_ => string.Empty);
        var relay = NewRelay(new BridgeConfig { ChannelId = "100" }, rcon);

        await relay.OnDiscordMessageAsync(new InboundMessage(100, 1, "Amy", false, false, [], "hi"), 999, CancellationToken.None);

        Assert.StartsWith("tellraw @a ", Assert.Single(rcon.Commands), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunCommandAsync_List_AnswersWithPlayers()
    {
        var relay = NewRelay(new BridgeConfig(), new FakeRcon(_ => "There are 2 of a max of 20 players online: Steve, Alex"));

        Assert.Equal("Online (2/20): Steve, Alex", await relay.RunCommandAsync(new InboundAction(InboundActionKind.ListPlayers), CancellationToken.None));
    }

    [Fact]
    public async Task RunCommandAsync_ListWhileServerOffInChinese_SaysOffline()
    {
        var relay = NewRelay(new BridgeConfig { Language = "zh-TW" }, new FakeRcon(_ => null));

        Assert.Equal("伺服器目前沒有開。", await relay.RunCommandAsync(new InboundAction(InboundActionKind.ListPlayers), CancellationToken.None));
    }

    [Fact]
    public async Task RunCommandAsync_ConsoleOutput_IsACodeBlockThatCannotBeClosedEarly()
    {
        var relay = NewRelay(new BridgeConfig(), new FakeRcon(_ => "§aDone ``` sneaky"));

        var answer = await relay.RunCommandAsync(new InboundAction(InboundActionKind.Console, "Amy", "say hi"), CancellationToken.None);

        Assert.Equal("```\nDone `\u200B`` sneaky\n```", answer);
    }

    private BridgeRelay NewRelay(BridgeConfig config, FakeRcon? rcon = null) =>
        new(config with { Presentation = DiscordPresentation.Webhook }, rcon ?? new FakeRcon(_ => null), _posted.Add, new BridgeLog(TextWriter.Null));
}
