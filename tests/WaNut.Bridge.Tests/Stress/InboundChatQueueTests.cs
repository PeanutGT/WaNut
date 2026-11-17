using WaNut.Bridge.Discord;
using WaNut.Bridge.Relay;

namespace WaNut.Bridge.Tests.Stress;

public sealed class InboundChatQueueTests
{
    private static InboundMessage Chat(int i) => new(100, 1, "amy", false, false, [], $"m{i}");

    [Fact]
    public async Task Enqueue_WhileTheGameIsFrozen_ReturnsAtOnceAndKeepsOrderAfterwards()
    {
        var release = new TaskCompletionSource();
        var handled = new List<string>();
        var queue = new InboundChatQueue(
            async (message, _) =>
            {
                await release.Task;
                handled.Add(message.Content);
            },
            new BridgeLog(TextWriter.Null));
        using var stop = new CancellationTokenSource();
        var run = queue.RunAsync(stop.Token);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 100; i++)
        {
            queue.Enqueue(Chat(i));
        }

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(1), "Enqueue waited for the game");
        release.SetResult();
        await WaitUntilAsync(() => handled.Count == 100);
        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        Assert.Equal(Enumerable.Range(0, 100).Select(i => $"m{i}"), handled);
    }

    [Fact]
    public async Task Enqueue_FarMoreThanTheGameTakes_DropsOldestAndKeepsTheNewest()
    {
        var release = new TaskCompletionSource();
        var handled = new List<string>();
        var log = new StringWriter();
        var queue = new InboundChatQueue(
            async (message, _) =>
            {
                await release.Task;
                lock (handled)
                {
                    handled.Add(message.Content);
                }
            },
            new BridgeLog(log));
        using var stop = new CancellationTokenSource();
        var run = queue.RunAsync(stop.Token);
        for (var i = 0; i < 1000; i++)
        {
            queue.Enqueue(Chat(i));
        }

        release.SetResult();
        await WaitUntilAsync(() => { lock (handled) { return handled.Contains("m999"); } });
        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        Assert.True(handled.Count <= InboundChatQueue.Capacity + 1);
        Assert.Equal(handled.OrderBy(m => int.Parse(m[1..], System.Globalization.CultureInfo.InvariantCulture)), handled);
        Assert.Contains("dropped", log.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_OneMessageFails_TheNextOnesStillGoThrough()
    {
        var handled = new List<string>();
        var queue = new InboundChatQueue(
            (message, _) =>
            {
                if (message.Content == "m1")
                {
                    throw new InvalidOperationException("boom");
                }

                lock (handled)
                {
                    handled.Add(message.Content);
                }

                return Task.CompletedTask;
            },
            new BridgeLog(TextWriter.Null));
        using var stop = new CancellationTokenSource();
        var run = queue.RunAsync(stop.Token);
        queue.Enqueue(Chat(0));
        queue.Enqueue(Chat(1));
        queue.Enqueue(Chat(2));

        await WaitUntilAsync(() => { lock (handled) { return handled.Count == 2; } });
        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        Assert.Equal(["m0", "m2"], handled);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "timed out");
            await Task.Delay(2);
        }
    }
}
