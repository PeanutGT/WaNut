using System.Net.Sockets;
using WaNut.Bridge.Rcon;

namespace WaNut.Bridge.Tests.Rcon;

public sealed class RconLinkTests
{
    [Fact]
    public async Task RunAsync_ServerOffThenUp_RetriesWithGrowingWaitsThenConnects()
    {
        await using var server = new FakeRconServer("pw", _ => "ok");
        var attempts = 0;
        var waits = new List<TimeSpan>();
        using var stop = new CancellationTokenSource();
        var link = new RconLink(
            token => ++attempts < 3
                ? throw new SocketException((int)SocketError.ConnectionRefused)
                : RconClient.ConnectAsync("127.0.0.1", server.Port, "pw", token),
            new BridgeLog(TextWriter.Null),
            (wait, _) =>
            {
                waits.Add(wait);
                return Task.CompletedTask;
            });
        var connected = new TaskCompletionSource();
        link.ConnectionChanged += up =>
        {
            if (up)
            {
                connected.TrySetResult();
            }
        };

        var run = link.RunAsync(stop.Token);
        await connected.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)], waits);
        Assert.True(link.IsConnected);
        Assert.Equal("ok", await link.TryExecuteAsync("list"));
        await stop.CancelAsync();
        await run;
        Assert.False(link.IsConnected);
    }

    [Fact]
    public async Task TryExecuteAsync_NotConnected_ReturnsNull()
    {
        var link = new RconLink(_ => throw new SocketException(), new BridgeLog(TextWriter.Null));

        Assert.Null(await link.TryExecuteAsync("list"));
    }

    [Fact]
    public async Task Wake_DuringLongWait_TriesAgainAtOnce()
    {
        var attempts = 0;
        var secondAttempt = new TaskCompletionSource();
        using var stop = new CancellationTokenSource();
        var link = new RconLink(
            _ =>
            {
                if (++attempts == 2)
                {
                    secondAttempt.TrySetResult();
                }

                throw new SocketException((int)SocketError.ConnectionRefused);
            },
            new BridgeLog(TextWriter.Null),
            (_, token) => Task.Delay(Timeout.Infinite, token));

        var run = link.RunAsync(stop.Token);
        await Task.Delay(100);
        link.Wake();

        await secondAttempt.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await stop.CancelAsync();
        await run;
    }
}
