using WaNut.Bridge.Rcon;

namespace WaNut.Bridge.Tests.Stress;

/// <summary>A Minecraft server that dies and comes back, with the real client and link on real loopback sockets.</summary>
public sealed class RconRecoveryTests
{
    private static async Task WaitUntilAsync(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "timed out waiting for " + what);
            await Task.Delay(5);
        }
    }

    private static RconLink NewLink(FlakyRconServer server, string password, List<TimeSpan>? waits, StringWriter? log = null) => new(
        token => RconClient.ConnectAsync("127.0.0.1", server.Port, password, token),
        new BridgeLog(log ?? TextWriter.Null),
        async (wait, token) =>
        {
            if (waits is not null)
            {
                lock (waits)
                {
                    waits.Add(wait);
                }
            }

            await Task.Delay(2, token);
        });

    [Fact]
    public async Task DisposeWhileCommandsAreQueuedBehindAFrozenOne_NobodyWaitsForever()
    {
        await using var server = new FlakyRconServer("pw") { Hang = true };
        server.Start();
        var client = await RconClient.ConnectAsync("127.0.0.1", server.Port, "pw", CancellationToken.None);
        var commands = Enumerable.Range(0, 4).Select(i => client.ExecuteAsync("say " + i, CancellationToken.None)).ToList();
        await WaitUntilAsync(() => { lock (server.Commands) { return server.Commands.Count >= 1; } }, "the first command to reach the server");

        await client.DisposeAsync();

        var all = Task.WhenAll(commands.Select(async c =>
        {
            try
            {
                await c;
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
            {
                // Any of these is a fine way to end; waiting forever is not.
            }
        }));
        await all.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ServerRestartedFiveTimes_EveryTimeTheLinkComesBackWithoutHelp()
    {
        await using var server = new FlakyRconServer("pw");
        server.Start();
        var link = NewLink(server, "pw", null);
        using var stop = new CancellationTokenSource();
        var run = link.RunAsync(stop.Token);
        await WaitUntilAsync(() => link.IsConnected, "first connection");

        for (var restart = 1; restart <= 5; restart++)
        {
            Assert.Equal("ok", await link.TryExecuteAsync("list"));
            server.Stop();

            // The next command notices the dead connection; it is lost (the server is down anyway), never thrown.
            Assert.Null(await link.TryExecuteAsync("list"));
            await WaitUntilAsync(() => !link.IsConnected, "the drop to be noticed");
            Assert.Null(await link.TryExecuteAsync("list"));

            server.Start();
            await WaitUntilAsync(() => link.IsConnected, $"reconnect after restart {restart}");
        }

        Assert.Equal("ok", await link.TryExecuteAsync("list"));
        await stop.CancelAsync();
        await run;
    }

    [Fact]
    public async Task ServerDownForALongTime_BackoffGrowsToTheCapAndStaysThere()
    {
        await using var server = new FlakyRconServer("pw");
        server.Start();
        server.Stop();
        var waits = new List<TimeSpan>();
        var link = NewLink(server, "pw", waits);
        using var stop = new CancellationTokenSource();
        var run = link.RunAsync(stop.Token);

        await WaitUntilAsync(() => { lock (waits) { return waits.Count >= 12; } }, "twelve failed attempts");
        await stop.CancelAsync();
        await run;

        var expected = new[] { 1, 2, 4, 8, 16, 32, 60, 60, 60, 60, 60, 60 }.Select(s => TimeSpan.FromSeconds(s));
        lock (waits)
        {
            Assert.Equal(expected, waits.Take(12));
        }
    }

    [Fact]
    public async Task WrongPassword_IsLoggedOnceNotOnEveryRetry()
    {
        await using var server = new FlakyRconServer("right");
        server.Start();
        var log = new StringWriter();
        var waits = new List<TimeSpan>();
        var link = NewLink(server, "wrong", waits, log);
        using var stop = new CancellationTokenSource();
        var run = link.RunAsync(stop.Token);

        await WaitUntilAsync(() => { lock (waits) { return waits.Count >= 6; } }, "six refused logins");
        await stop.CancelAsync();
        await run;

        Assert.Equal(1, log.ToString().Split("refused the password").Length - 1);
        Assert.False(link.IsConnected);
    }

    [Fact]
    public async Task ManyCommandsDuringARestart_NoneThrowAndLinkRecovers()
    {
        await using var server = new FlakyRconServer("pw");
        server.Start();
        var link = NewLink(server, "pw", null);
        using var stop = new CancellationTokenSource();
        var run = link.RunAsync(stop.Token);
        await WaitUntilAsync(() => link.IsConnected, "first connection");

        var senders = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            for (var i = 0; i < 200; i++)
            {
                await link.TryExecuteAsync("tellraw @a x");
                await Task.Yield();
            }
        })).ToList();
        for (var i = 0; i < 3; i++)
        {
            await Task.Delay(20);
            server.Stop();
            await Task.Delay(20);
            server.Start();
        }

        await Task.WhenAll(senders);
        // The link may still hold the last dead connection (nobody has used it since); the first command finds out.
        await WaitUntilAsync(() => link.IsConnected, "recovery");
        string? answer = null;
        await WaitUntilAsync(() => (answer = link.TryExecuteAsync("list").GetAwaiter().GetResult()) == "ok", "a working connection");
        await stop.CancelAsync();
        await run;
    }

    [Fact]
    public async Task Wake_ServerRestartedWhileLinkStillHoldsTheDeadConnection_ReconnectsBeforeTheNextCommand()
    {
        await using var server = new FlakyRconServer("pw");
        server.Start();
        var link = NewLink(server, "pw", null);
        using var stop = new CancellationTokenSource();
        var run = link.RunAsync(stop.Token);
        await WaitUntilAsync(() => link.IsConnected, "first connection");

        server.Stop();
        server.Start();
        link.Wake();
        await WaitUntilAsync(() => server.Connections >= 2, "a second connection");
        await WaitUntilAsync(() => link.IsConnected, "link up again");

        Assert.Equal("ok", await link.TryExecuteAsync("list"));
        await stop.CancelAsync();
        await run;
    }
}
