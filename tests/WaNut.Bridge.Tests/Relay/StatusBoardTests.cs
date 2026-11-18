using System.Text.Json;
using WaNut.Bridge.Relay;

namespace WaNut.Bridge.Tests.Relay;

public sealed class StatusBoardTests
{
    [Theory]
    [InlineData("TPS from last 1m, 5m, 15m: 19.8, 20.0, 20.0", 19.8)]
    [InlineData("TPS from last 5s, 1m, 5m, 15m: *20.0, 19.5, 20.0, 20.0", 20.0)]
    [InlineData("TPS from last 1m, 5m, 15m: >21.3, 20.0, 20.0", 20.0)]
    public void TryParseTps_PaperOutput_GivesTheNewestNumberCappedAt20(string output, double expected) =>
        Assert.Equal(expected, TickStats.TryParseTps(output)?.Tps);

    [Fact]
    public void TryParseTickQuery_Vanilla_GivesMsptAndTps()
    {
        var stats = TickStats.TryParseTickQuery("The game is running normally\nAverage time per tick: 2.5ms (Target: 50.0ms)");

        Assert.Equal(2.5, stats?.Mspt);
        Assert.Equal(20, stats?.Tps);
    }

    [Fact]
    public void TryParse_UnknownCommand_IsNull()
    {
        Assert.Null(TickStats.TryParseTps("Unknown or incomplete command, see below for error"));
        Assert.Null(TickStats.TryParseTickQuery("Unknown or incomplete command"));
    }

    [Fact]
    public void Build_Online_HasStatusPlayersTpsAndUptime_AndNoPings()
    {
        var data = new StatusBoardData(PlayerList.TryParse("There are 2 of a max of 20 players online: Steve, Alex"), new TickStats(19.9, 31.2), TimeSpan.FromMinutes(125));

        using var json = JsonDocument.Parse(StatusBoardMessage.Build(data, chinese: false, DateTimeOffset.UnixEpoch));

        var fields = json.RootElement.GetProperty("embeds")[0].GetProperty("fields").EnumerateArray().ToDictionary(f => f.GetProperty("name").GetString()!, f => f.GetProperty("value").GetString()!);
        Assert.Contains("Online", fields["Status"], StringComparison.Ordinal);
        Assert.Equal("2 / 20\nSteve, Alex", fields["Players"]);
        Assert.Equal("19.9", fields["TPS"]);
        Assert.Equal("31.2 ms", fields["MSPT"]);
        Assert.Equal("2h 5m", fields["Uptime"]);
        Assert.Empty(json.RootElement.GetProperty("allowed_mentions").GetProperty("parse").EnumerateArray());
    }

    [Fact]
    public void Build_Offline_ShowsOnlyTheStatus_InChinese()
    {
        using var json = JsonDocument.Parse(StatusBoardMessage.Build(new StatusBoardData(null, null, null), chinese: true, DateTimeOffset.UnixEpoch));

        var fields = json.RootElement.GetProperty("embeds")[0].GetProperty("fields");
        Assert.Equal(1, fields.GetArrayLength());
        Assert.Contains("未開啟", fields[0].GetProperty("value").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void FormatUptime_PicksTheLargestUnits() =>
        Assert.Equal("2d 3h", StatusBoardMessage.FormatUptime(TimeSpan.FromHours(51), chinese: false));
}

public sealed class StatusBoardRunTests
{
    private sealed class OfflineRcon : WaNut.Bridge.Rcon.IRconCommands
    {
        public bool IsConnected => false;

        public Task<string?> TryExecuteAsync(string command, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

    /// <summary>Answers like Discord: PATCH on a message in <see cref="MissingMessages"/> is 404, every POST makes message 555.</summary>
    private sealed class Handler : HttpMessageHandler
    {
        public List<string> Calls { get; } = [];

        public HashSet<string> MissingMessages { get; } = [];

        public HashSet<string> MissingChannels { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Calls.Add($"{request.Method} {path}");
            var gone = MissingMessages.Contains(path) || MissingChannels.Any(c => path.StartsWith($"/api/v10/channels/{c}/", StringComparison.Ordinal));
            return Task.FromResult(request.Method != HttpMethod.Post && gone
                ? new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)
                : request.Method == HttpMethod.Post && MissingChannels.Any(c => path.StartsWith($"/api/v10/channels/{c}/", StringComparison.Ordinal))
                    ? new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)
                    : new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("""{"id":"555"}""") });
        }
    }

    private sealed record Setup(StatusBoard Board, Handler Handler, string File);

    private static Setup Create(ulong? defaultChannel = 100, string? stored = null, string? legacy = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "status-board.json");
        if (stored is not null)
        {
            File.WriteAllText(file, stored);
        }

        if (legacy is not null)
        {
            File.WriteAllText(Path.Combine(dir, "status-board.id"), legacy);
        }

        var handler = new Handler();
        var log = new BridgeLog(TextWriter.Null);
        var relay = new BridgeRelay(new WaNut.Core.Bridge.BridgeConfig(), new OfflineRcon(), _ => { }, log);
        return new Setup(new StatusBoard(new WaNut.Bridge.Discord.DiscordRest(new HttpClient(handler), "bot"), defaultChannel, relay, false, file, log), handler, file);
    }

    private static async Task RunBrieflyAsync(StatusBoard board)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => board.RunAsync(cts.Token));
    }

    [Fact]
    public async Task RunAsync_RememberedMessageDeleted_PostsANewOneAndRemembersIt()
    {
        var s = Create(stored: """{"channelId":"100","messageId":"123"}""");
        s.Handler.MissingMessages.Add("/api/v10/channels/100/messages/123");

        await RunBrieflyAsync(s.Board);

        Assert.Equal(["PATCH /api/v10/channels/100/messages/123", "POST /api/v10/channels/100/messages"], s.Handler.Calls);
        Assert.Contains("\"messageId\":\"555\"", File.ReadAllText(s.File), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_OldIdFile_KeepsEditingThatMessageInTheBridgeChannel()
    {
        var s = Create(legacy: "123");

        await RunBrieflyAsync(s.Board);

        Assert.Equal(["PATCH /api/v10/channels/100/messages/123"], s.Handler.Calls);
    }

    [Fact]
    public async Task RunAsync_NoPlaceAndNoSetting_DoesNothingUntilStatusIsUsed()
    {
        var s = Create(defaultChannel: null);

        await RunBrieflyAsync(s.Board);

        Assert.Empty(s.Handler.Calls);
    }

    [Fact]
    public async Task RunAsync_ChannelDeleted_ForgetsThePlaceSoNothingIsPostedAgain()
    {
        var s = Create(stored: """{"channelId":"100","messageId":"123"}""");
        s.Handler.MissingChannels.Add("100");

        await RunBrieflyAsync(s.Board);

        Assert.Equal(["PATCH /api/v10/channels/100/messages/123", "POST /api/v10/channels/100/messages"], s.Handler.Calls);
        Assert.False(File.Exists(s.File));
    }

    [Fact]
    public async Task PlaceAsync_NoBoardYet_PostsOneAndRemembersIt()
    {
        var s = Create(defaultChannel: null);

        var placement = await s.Board.PlaceAsync(200, CancellationToken.None);

        Assert.Equal(StatusPlacement.Created, placement);
        Assert.Equal(["POST /api/v10/channels/200/messages"], s.Handler.Calls);
        Assert.Contains("\"channelId\":\"200\"", File.ReadAllText(s.File), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlaceAsync_BoardAlreadyInThatChannel_EditsItInsteadOfPostingASecond()
    {
        var s = Create(defaultChannel: null, stored: """{"channelId":"200","messageId":"123"}""");
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => s.Board.RunAsync(cts.Token));
        s.Handler.Calls.Clear();

        var placement = await s.Board.PlaceAsync(200, CancellationToken.None);

        Assert.Equal(StatusPlacement.AlreadyHere, placement);
        Assert.Equal(["PATCH /api/v10/channels/200/messages/123"], s.Handler.Calls);
    }

    [Fact]
    public async Task PlaceAsync_BoardInAnotherChannel_PostsHereAndDeletesTheOldOne()
    {
        var s = Create(defaultChannel: null, stored: """{"channelId":"100","messageId":"123"}""");
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => s.Board.RunAsync(cts.Token));
        s.Handler.Calls.Clear();

        var placement = await s.Board.PlaceAsync(200, CancellationToken.None);

        Assert.Equal(StatusPlacement.Moved, placement);
        Assert.Equal(["POST /api/v10/channels/200/messages", "DELETE /api/v10/channels/100/messages/123"], s.Handler.Calls);
        Assert.Contains("\"channelId\":\"200\"", File.ReadAllText(s.File), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlaceAsync_RememberedMessageDeleted_PostsANewOne()
    {
        var s = Create(defaultChannel: null, stored: """{"channelId":"200","messageId":"123"}""");
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => s.Board.RunAsync(cts.Token));
        s.Handler.MissingMessages.Add("/api/v10/channels/200/messages/123");
        s.Handler.Calls.Clear();

        var placement = await s.Board.PlaceAsync(200, CancellationToken.None);

        Assert.Equal(StatusPlacement.Created, placement);
        Assert.Equal(["PATCH /api/v10/channels/200/messages/123", "POST /api/v10/channels/200/messages"], s.Handler.Calls);
    }
}
