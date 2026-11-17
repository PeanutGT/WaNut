using WaNut.Bridge.Discord;

namespace WaNut.Bridge.Tests.Stress;

/// <summary>Chat flooding against the real poster and outbox with a fake Discord that rate limits like the real one.</summary>
public sealed class OutboxFloodTests
{
    private static string Line(int i) => $"line-{i:D6}";

    private static int Number(string line) => int.Parse(line[5..], System.Globalization.CultureInfo.InvariantCulture);

    private static IEnumerable<int> Numbers(FakeDiscordWebhook webhook) =>
        webhook.Accepted.SelectMany(post => post.Split('\n')).Select(Number);

    private static (DiscordOutbox Outbox, FakeDiscordWebhook Webhook, StringWriter Log) Create(bool bucketHeaders = true)
    {
        var webhook = new FakeDiscordWebhook { SendBucketHeaders = bucketHeaders };
        var http = new HttpClient(webhook);
        var log = new StringWriter();
        var poster = DiscordHttpPoster.ForWebhook(http, "https://discord.com/api/webhooks/1/abc");
        return (new DiscordOutbox(poster, new BridgeLog(log), webhook.Delay), webhook, log);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Flood_WavesBelowTheQueueSize_LoseNothingAndKeepOrder(bool bucketHeaders)
    {
        // 40 waves of 400 lines, each from a different player, so no two lines can share a post: the worst case for the limit.
        var (outbox, webhook, _) = Create(bucketHeaders);
        var run = outbox.RunAsync(CancellationToken.None);
        var sent = 0;
        for (var wave = 0; wave < 40; wave++)
        {
            for (var i = 0; i < 400; i++, sent++)
            {
                outbox.Enqueue(new OutgoingMessage(Line(sent), $"Player{sent % 7}"));
            }

            await WaitUntilAsync(() => webhook.Accepted.Count >= sent);
        }

        outbox.Complete();
        await run.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(Enumerable.Range(0, sent), webhook.Accepted.Select(Number).ToList());
        Assert.True(webhook.LongestContent <= DiscordBatcher.MaxContentLength);
        if (bucketHeaders)
        {
            Assert.Equal(0, webhook.TooManyRequests);
        }

        // 16000 posts at 5 per 2 seconds: the virtual clock shows the limit was kept, not just survived.
        Assert.True(webhook.Now >= TimeSpan.FromSeconds(16000 / 5 * 2 - 2), $"finished after only {webhook.Now}");
    }

    [Fact]
    public async Task Flood_SameSenderLines_MergeIntoFewPostsWithoutLoss()
    {
        var (outbox, webhook, _) = Create();
        var run = outbox.RunAsync(CancellationToken.None);
        var sent = 0;
        for (var wave = 0; wave < 50; wave++)
        {
            for (var i = 0; i < 400; i++, sent++)
            {
                outbox.Enqueue(new OutgoingMessage(Line(sent)));
            }

            await WaitUntilAsync(() => Numbers(webhook).Count() >= sent);
        }

        outbox.Complete();
        await run.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(Enumerable.Range(0, sent), Numbers(webhook).ToList());
        Assert.True(webhook.Accepted.Count < sent / 10, $"{webhook.Accepted.Count} posts for {sent} lines");
        Assert.True(webhook.LongestContent <= DiscordBatcher.MaxContentLength);
    }

    [Fact]
    public async Task Flood_FarBeyondTheQueueSize_DropsOldestKeepsOrderAndNewestLineArrives()
    {
        var (outbox, webhook, log) = Create();
        var run = outbox.RunAsync(CancellationToken.None);
        const int total = 20000;
        for (var i = 0; i < total; i++)
        {
            outbox.Enqueue(new OutgoingMessage(Line(i), $"Player{i % 7}"));
            if (i % 1000 == 0)
            {
                await Task.Yield();
            }
        }

        outbox.Complete();
        await run.WaitAsync(TimeSpan.FromSeconds(60));

        var numbers = Numbers(webhook).ToList();
        Assert.Equal(total - 1, numbers[^1]);
        Assert.Equal(numbers.OrderBy(n => n), numbers);
        Assert.Equal(numbers.Count, numbers.Distinct().Count());
        Assert.Contains("old messages were dropped", log.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Outage_ShorterThanTheRetries_LosesNothing()
    {
        var (outbox, webhook, _) = Create();
        webhook.Outage = (TimeSpan.Zero, TimeSpan.FromSeconds(8));
        outbox.Enqueue(new OutgoingMessage(Line(0)));
        outbox.Enqueue(new OutgoingMessage(Line(1), "Alex"));
        outbox.Complete();

        await outbox.RunAsync(CancellationToken.None);

        Assert.Equal([0, 1], Numbers(webhook).ToList());
    }

    [Fact]
    public async Task Outage_LongerThanTheRetries_LaterMessagesStillArrive()
    {
        // The post being sent when Discord vanishes is dropped after five tries (30 s of waiting); the bridge itself keeps going.
        var (outbox, webhook, log) = Create();
        webhook.Outage = (TimeSpan.Zero, TimeSpan.FromMinutes(5));
        var run = outbox.RunAsync(CancellationToken.None);
        for (var i = 0; i < 5; i++)
        {
            outbox.Enqueue(new OutgoingMessage(Line(i), $"Player{i}"));
        }

        await WaitUntilAsync(() => webhook.Requests >= 5 * DiscordOutbox.MaxAttempts);
        webhook.Advance(TimeSpan.FromMinutes(6));
        outbox.Enqueue(new OutgoingMessage(Line(100), "Late"));
        outbox.Complete();
        await run.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(100, Numbers(webhook).Last());
        Assert.Contains("Gave up on a Discord message", log.ToString(), StringComparison.Ordinal);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "timed out waiting for the outbox");
            await Task.Delay(1);
        }
    }
}
