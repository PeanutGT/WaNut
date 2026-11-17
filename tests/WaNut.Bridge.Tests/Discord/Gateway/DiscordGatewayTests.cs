using WaNut.Bridge.Discord;
using WaNut.Bridge.Discord.Gateway;

namespace WaNut.Bridge.Tests.Discord.Gateway;

public sealed class DiscordGatewayTests
{
    private const string Hello = """{"op":10,"d":{"heartbeat_interval":41250}}""";
    private const string Ready = """{"op":0,"s":1,"t":"READY","d":{"session_id":"sess","resume_gateway_url":"wss://resume.example","user":{"id":"999"},"application":{"id":"777"}}}""";
    private const string Message = """{"op":0,"s":2,"t":"MESSAGE_CREATE","d":{"type":0,"channel_id":"100","content":"hi","author":{"id":"1","username":"amy"},"member":{"roles":[]}}}""";

    private static readonly Func<TimeSpan, CancellationToken, Task> NeverEnds = (_, token) => Task.Delay(Timeout.Infinite, token);

    [Fact]
    public async Task RunAsync_NewSession_IdentifiesThenReportsReadyAndMessages()
    {
        var socket = new FakeGatewaySocket();
        using var stop = new CancellationTokenSource();
        var gateway = new DiscordGateway("tok", new BridgeLog(TextWriter.Null), (_, _) => Task.FromResult<IGatewaySocket>(socket), NeverEnds);
        var ready = new TaskCompletionSource<string>();
        var received = new TaskCompletionSource<InboundMessage>();
        gateway.Ready += id =>
        {
            ready.TrySetResult(id);
            return Task.CompletedTask;
        };
        gateway.MessageReceived += message =>
        {
            received.TrySetResult(message);
            return Task.CompletedTask;
        };

        var run = gateway.RunAsync(stop.Token);
        socket.Push(Hello);
        await socket.FirstSend.Task.WaitAsync(TimeSpan.FromSeconds(5));
        socket.Push(Ready);
        socket.Push(Message);

        Assert.Equal("777", await ready.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("hi", (await received.Task.WaitAsync(TimeSpan.FromSeconds(5))).Content);
        Assert.Contains("\"op\":2", socket.Sent[0], StringComparison.Ordinal);
        Assert.Contains($"\"intents\":{GatewayPayloads.Intents}", socket.Sent[0], StringComparison.Ordinal);
        Assert.Equal(999UL, gateway.BotUserId);
        Assert.True(gateway.IsConnected);
        await stop.CancelAsync();
        await run;
    }

    [Fact]
    public async Task RunAsync_ReconnectRequested_ResumesAtTheResumeUrlWithTheLastSequence()
    {
        var first = new FakeGatewaySocket();
        var second = new FakeGatewaySocket();
        var urls = new List<Uri>();
        using var stop = new CancellationTokenSource();
        var gateway = new DiscordGateway(
            "tok",
            new BridgeLog(TextWriter.Null),
            (url, _) =>
            {
                urls.Add(url);
                return Task.FromResult<IGatewaySocket>(urls.Count == 1 ? first : second);
            },
            (wait, token) => wait < TimeSpan.FromSeconds(30) ? Task.CompletedTask : Task.Delay(Timeout.Infinite, token));

        var run = gateway.RunAsync(stop.Token);
        first.Push(Hello);
        await first.FirstSend.Task.WaitAsync(TimeSpan.FromSeconds(5));
        first.Push(Ready);
        first.Push(Message);
        first.Push("""{"op":7,"d":null}""");
        second.Push(Hello);
        await second.FirstSend.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("wss://resume.example/?v=10&encoding=json", urls[1].ToString());
        Assert.Contains("\"op\":6", second.Sent[0], StringComparison.Ordinal);
        Assert.Contains("\"session_id\":\"sess\"", second.Sent[0], StringComparison.Ordinal);
        Assert.Contains("\"seq\":2", second.Sent[0], StringComparison.Ordinal);
        await stop.CancelAsync();
        await run;
    }

    [Fact]
    public async Task RunAsync_DisallowedIntents_StopsAndExplains()
    {
        var socket = new FakeGatewaySocket();
        var log = new StringWriter();
        var gateway = new DiscordGateway("tok", new BridgeLog(log), (_, _) => Task.FromResult<IGatewaySocket>(socket), NeverEnds);

        var run = gateway.RunAsync(CancellationToken.None);
        socket.Push(Hello);
        await socket.FirstSend.Task.WaitAsync(TimeSpan.FromSeconds(5));
        socket.Close(GatewayCloseCodes.DisallowedIntents);

        await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("Message Content", log.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_HeartbeatNotAnswered_DropsTheConnection()
    {
        var socket = new FakeGatewaySocket();
        using var stop = new CancellationTokenSource();
        var connects = 0;
        var gateway = new DiscordGateway(
            "tok",
            new BridgeLog(TextWriter.Null),
            (_, _) =>
            {
                if (++connects > 1)
                {
                    stop.Cancel();
                }

                return Task.FromResult<IGatewaySocket>(socket);
            },
            (_, _) => Task.CompletedTask);

        var run = gateway.RunAsync(stop.Token);
        socket.Push(Hello);

        await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(socket.Aborted);
        Assert.Contains(socket.Sent, s => s.Contains("\"op\":1", StringComparison.Ordinal));
    }
}
