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
    public void OnLogLine_Forwarded_IsLoggedWithWhoButNeverTheMessageText()
    {
        var writer = new StringWriter();
        var relay = new BridgeRelay(new BridgeConfig(), new FakeRcon(_ => null), _posted.Add, new BridgeLog(writer));

        relay.OnLogLine(Prefix + "<Steve> my secret plan");
        relay.OnLogLine(Prefix + "Steve joined the game");

        var text = writer.ToString();
        Assert.Contains("Minecraft to Discord: Chat Steve.", text, StringComparison.Ordinal);
        Assert.Contains("Minecraft to Discord: Joined Steve.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", text, StringComparison.Ordinal);
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
    public async Task OnDiscordMessageAsync_FileOnlyMessage_SendsTheFileLine()
    {
        var rcon = new FakeRcon(_ => string.Empty);
        var relay = NewRelay(new BridgeConfig { ChannelId = "100" }, rcon);
        var file = new InboundAttachment("https://cdn.discordapp.com/a/b.png", "b.png", "image/png", 5);

        await relay.OnDiscordMessageAsync(new InboundMessage(100, 1, "Amy", false, false, [], "", [file]), 999, CancellationToken.None);

        var command = Assert.Single(rcon.Commands);
        Assert.Contains("[Image: b.png]", command, StringComparison.Ordinal);
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

    [Fact]
    public async Task RunCommandAsync_ConsoleUseAndDenial_AreBothAudited()
    {
        var writer = new StringWriter();
        var relay = new BridgeRelay(new BridgeConfig(), new FakeRcon(_ => "ok"), _posted.Add, new BridgeLog(writer));

        await relay.RunCommandAsync(new InboundAction(InboundActionKind.Console, "Amy", "op Amy", 42UL), CancellationToken.None);
        await relay.RunCommandAsync(new InboundAction(InboundActionKind.ConsoleDenied, "Bob", "stop", 7UL), CancellationToken.None);

        var text = writer.ToString();
        Assert.Contains("[INFO] Remote console: Amy (42) ran \"op Amy\".", text, StringComparison.Ordinal);
        Assert.Contains("[WARN] Remote console denied: Bob (7) tried \"stop\".", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportCrash_PostsTheCause_UnlessServerNoticesAreOff()
    {
        var relay = NewRelay(new BridgeConfig());
        relay.OnLogLine(Prefix + "**** FAILED TO BIND TO PORT!");
        relay.OnLogLine(Prefix + "The exception was: java.net.BindException: Address already in use");

        relay.ReportCrash(DateTime.UtcNow);

        Assert.Contains(_posted, m => m.Text.Contains("Port", StringComparison.Ordinal) && m.Text.Contains("crashed", StringComparison.Ordinal));

        var quiet = NewRelay(new BridgeConfig { Filters = new BridgeFilters { ServerState = false } });
        _posted.Clear();
        quiet.ReportCrash(DateTime.UtcNow);
        Assert.Empty(_posted);
    }

    private BridgeRelay NewRelay(BridgeConfig config, FakeRcon? rcon = null) =>
        new(config with { Presentation = DiscordPresentation.Webhook }, rcon ?? new FakeRcon(_ => null), _posted.Add, new BridgeLog(TextWriter.Null));
}
