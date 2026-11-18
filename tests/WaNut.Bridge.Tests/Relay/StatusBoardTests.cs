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

    private sealed class Handler : HttpMessageHandler
    {
        public List<string> Calls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");
            return Task.FromResult(request.Method == HttpMethod.Patch
                ? new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)
                : new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("""{"id":"555"}""") });
        }
    }

    [Fact]
    public async Task RunAsync_RememberedMessageDeleted_PostsANewOneAndRemembersIt()
    {
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".id");
        File.WriteAllText(file, "123");
        var handler = new Handler();
        var log = new BridgeLog(TextWriter.Null);
        var relay = new BridgeRelay(new WaNut.Core.Bridge.BridgeConfig(), new OfflineRcon(), _ => { }, log);
        var board = new StatusBoard(new WaNut.Bridge.Discord.DiscordRest(new HttpClient(handler), "bot"), 100, relay, false, file, log);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => board.RunAsync(cts.Token));

        try
        {
            Assert.Equal(["PATCH /api/v10/channels/100/messages/123", "POST /api/v10/channels/100/messages"], handler.Calls);
            Assert.Equal("555", File.ReadAllText(file));
        }
        finally
        {
            File.Delete(file);
        }
    }
}
