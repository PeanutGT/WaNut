using System.Net.WebSockets;
using WaNut.Bridge.Discord;
using WaNut.Bridge.Discord.Gateway;
using WaNut.Bridge.Tests.Discord.Gateway;

namespace WaNut.Bridge.Tests.Stress;

/// <summary>The Gateway client against a fake Gateway that drops, lies and sends garbage.</summary>
public sealed class GatewayRecoveryTests
{
    // An hour between heartbeats: the heartbeat wait never ends, so only the reconnect backoff waits are recorded.
    private const string Hello = """{"op":10,"d":{"heartbeat_interval":3600000}}""";
    private const string Ready = """{"op":0,"s":1,"t":"READY","d":{"session_id":"sess","resume_gateway_url":"wss://resume.example","user":{"id":"999"},"application":{"id":"777"}}}""";

    private static string Message(int sequence, string text) =>
        "{\"op\":0,\"s\":" + sequence + ",\"t\":\"MESSAGE_CREATE\",\"d\":{\"type\":0,\"channel_id\":\"100\",\"content\":\"" + text
        + "\",\"author\":{\"id\":\"1\",\"username\":\"amy\"},\"member\":{\"roles\":[]}}}";

    private sealed class Scripted
    {
        private readonly Queue<object> _connections = new();
        private readonly List<TimeSpan> _waits = [];

        public List<Uri> Urls { get; } = [];

        public List<FakeGatewaySocket> Sockets { get; } = [];

        public void Add(object socketOrException) => _connections.Enqueue(socketOrException);

        public TimeSpan[] Waits
        {
            get
            {
                lock (_waits)
                {
                    return [.. _waits];
                }
            }
        }

        public Task<IGatewaySocket> Connect(Uri url, CancellationToken cancellationToken)
        {
            Urls.Add(url);
            if (!_connections.TryDequeue(out var next))
            {
                // Nothing scripted any more: the connection hangs until the test ends.
                return Task.Delay(Timeout.Infinite, cancellationToken).ContinueWith<IGatewaySocket>(_ => throw new OperationCanceledException(), cancellationToken);
            }

            if (next is Exception ex)
            {
                return Task.FromException<IGatewaySocket>(ex);
            }

            var socket = (FakeGatewaySocket)next;
            Sockets.Add(socket);
            return Task.FromResult<IGatewaySocket>(socket);
        }

        public async Task Delay(TimeSpan wait, CancellationToken cancellationToken)
        {
            if (wait > TimeSpan.FromMinutes(5))
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            lock (_waits)
            {
                _waits.Add(wait);
            }

            await Task.Yield();
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "timed out waiting for " + what);
            await Task.Delay(2);
        }
    }

    private static (DiscordGateway Gateway, Scripted Script, List<string> Messages) Create(StringWriter? log = null)
    {
        var script = new Scripted();
        var gateway = new DiscordGateway("tok", new BridgeLog(log ?? TextWriter.Null), script.Connect, script.Delay);
        var messages = new List<string>();
        gateway.MessageReceived += message =>
        {
            lock (messages)
            {
                messages.Add(message.Content);
            }

            return Task.CompletedTask;
        };
        return (gateway, script, messages);
    }

    private static FakeGatewaySocket SignedIn(params string[] frames)
    {
        var socket = new FakeGatewaySocket();
        socket.Push(Hello);
        foreach (var frame in frames)
        {
            socket.Push(frame);
        }

        return socket;
    }

    [Fact]
    public async Task ConnectFailsRepeatedly_RetriesWithGrowingWaitsCappedAtAMinute()
    {
        var (gateway, script, _) = Create();
        for (var i = 0; i < 9; i++)
        {
            script.Add(new HttpRequestException("no route to host"));
        }

        using var stop = new CancellationTokenSource();
        var run = gateway.RunAsync(stop.Token);
        await WaitUntilAsync(() => script.Waits.Length >= 9, "nine backoff waits");
        await stop.CancelAsync();
        await run;

        Assert.Equal(new[] { 1, 2, 4, 8, 16, 32, 60, 60, 60 }.Select(s => TimeSpan.FromSeconds(s)), script.Waits.Take(9));
    }

    [Fact]
    public async Task ConnectionDropsMidSession_ResumesWithTheLastSequenceAndKeepsDelivering()
    {
        var (gateway, script, messages) = Create();
        var first = SignedIn(Ready, Message(2, "one"), Message(3, "two"));
        var second = SignedIn("""{"op":0,"s":4,"t":"RESUMED","d":{}}""", Message(5, "three"));
        script.Add(first);
        script.Add(second);
        using var stop = new CancellationTokenSource();
        var run = gateway.RunAsync(stop.Token);

        await WaitUntilAsync(() => { lock (messages) { return messages.Count == 2; } }, "two messages");
        first.Abort();
        await WaitUntilAsync(() => { lock (messages) { return messages.Count == 3; } }, "the message after the resume");
        await stop.CancelAsync();
        await run;

        Assert.Equal(["one", "two", "three"], messages);
        Assert.Contains("\"op\":6", second.Sent[0], StringComparison.Ordinal);
        Assert.Contains("\"seq\":3", second.Sent[0], StringComparison.Ordinal);
        Assert.Equal("wss://resume.example/?v=10&encoding=json", script.Urls[1].ToString());
    }

    [Fact]
    public async Task DiscordSaysInvalidSessionAndCannotResume_IdentifiesAgainInsteadOfResuming()
    {
        var (gateway, script, _) = Create();
        var first = SignedIn(Ready, """{"op":9,"d":false}""");
        var second = SignedIn(Ready);
        script.Add(first);
        script.Add(second);
        using var stop = new CancellationTokenSource();
        var run = gateway.RunAsync(stop.Token);

        await WaitUntilAsync(() => second.Sent.Count > 0, "the second connection to sign in");
        await stop.CancelAsync();
        await run;

        Assert.Contains("\"op\":2", second.Sent[0], StringComparison.Ordinal);
        Assert.Equal(DiscordGateway.DefaultUrl, script.Urls[1]);
    }

    [Fact]
    public async Task ThirtyConnectionsThatDieRightAway_TheBridgeStaysUpAndTheThirtyFirstWorks()
    {
        var (gateway, script, messages) = Create();
        for (var i = 0; i < 30; i++)
        {
            var flaky = SignedIn(i % 2 == 0 ? Ready : """{"op":7,"d":null}""");
            flaky.Close(1006);
            script.Add(flaky);
        }

        script.Add(SignedIn(Ready, Message(2, "finally")));
        using var stop = new CancellationTokenSource();
        var run = gateway.RunAsync(stop.Token);

        await WaitUntilAsync(() => { lock (messages) { return messages.Count == 1; } }, "the message on the last connection");
        await stop.CancelAsync();
        await run;

        Assert.Equal(["finally"], messages);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("""{"no_op_here":true}""")]
    [InlineData("""{"op":99999999999}""")]
    [InlineData("""{"op":0,"s":99999999999,"t":"MESSAGE_CREATE","d":{}}""")]
    [InlineData("""{"op":0,"s":2,"t":"MESSAGE_CREATE","d":{"type":99999999999,"author":{}}}""")]
    [InlineData("""{"op":0,"s":2,"t":"MESSAGE_CREATE","d":{"type":0,"channel_id":12,"author":{"id":5},"content":7}}""")]
    [InlineData("""{"op":0,"s":2,"t":"INTERACTION_CREATE","d":{"type":2,"member":{"user":{}},"data":{"options":[{"name":"x"},{"name":"y","value":{"deep":[1,2]}}]}}}""")]
    [InlineData("""{"op":0,"s":1,"t":"READY","d":{"session_id":"s","resume_gateway_url":"not a url ::","user":{"id":"9"}}}""")]
    [InlineData("""{"op":0,"s":1,"t":"READY","d":{"session_id":"s"}}""")]
    [InlineData("""{"op":10,"d":{}}""")]
    public async Task GarbledFrame_NeverEndsTheGatewayAndTheNextConnectionWorks(string garbage)
    {
        var (gateway, script, messages) = Create();
        // Whether the garbage ends the connection or is just skipped, a real message right behind it must arrive.
        script.Add(SignedIn(garbage, Ready, Message(2, "still alive")));
        script.Add(SignedIn(Ready, Message(2, "still alive")));
        using var stop = new CancellationTokenSource();
        var run = gateway.RunAsync(stop.Token);

        await WaitUntilAsync(() => { lock (messages) { return messages.Count >= 1; } }, "a message after the garbage");
        Assert.False(run.IsCompleted, "the Gateway task ended");
        await stop.CancelAsync();
        await run;
    }

    [Fact]
    public async Task TokenRefused_StopsOnceWithoutHammeringDiscord()
    {
        var log = new StringWriter();
        var (gateway, script, _) = Create(log);
        var refused = SignedIn();
        refused.Close(4004);
        script.Add(refused);
        script.Add(SignedIn(Ready));

        await gateway.RunAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Single(script.Urls);
        Assert.Contains("[ERROR]", log.ToString(), StringComparison.Ordinal);
        Assert.False(gateway.IsConnected);
    }

    [Fact]
    public async Task SocketThrowsWebSocketException_IsReconnectedLikeADrop()
    {
        var (gateway, script, messages) = Create();
        script.Add(new WebSocketException("The remote party closed the WebSocket connection without completing the close handshake."));
        script.Add(SignedIn(Ready, Message(2, "back")));
        using var stop = new CancellationTokenSource();
        var run = gateway.RunAsync(stop.Token);

        await WaitUntilAsync(() => { lock (messages) { return messages.Count == 1; } }, "the message");
        await stop.CancelAsync();
        await run;
    }
}
