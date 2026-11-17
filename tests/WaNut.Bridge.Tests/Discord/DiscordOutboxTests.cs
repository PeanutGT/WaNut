using WaNut.Bridge.Discord;

namespace WaNut.Bridge.Tests.Discord;

public sealed class DiscordOutboxTests
{
    private readonly List<TimeSpan> _waits = [];

    [Fact]
    public async Task RunAsync_BurstOfLines_WaitsTheWindowAndSendsOnePost()
    {
        var poster = new FakePoster();
        var outbox = NewOutbox(poster);
        outbox.Enqueue(new("Steve joined"));
        outbox.Enqueue(new("Alex joined"));
        outbox.Complete();

        await outbox.RunAsync(CancellationToken.None);

        Assert.Equal("Steve joined\nAlex joined", Assert.Single(poster.Posts).Content);
        Assert.Equal([DiscordOutbox.BatchWindow], _waits);
    }

    [Fact]
    public async Task RunAsync_RateLimited_WaitsRetryAfterAndSendsTheSamePostAgain()
    {
        var poster = new FakePoster(new DiscordPostResult(DiscordPostOutcome.RateLimited, TimeSpan.FromSeconds(3)));
        var outbox = NewOutbox(poster);
        outbox.Enqueue(new("hello"));
        outbox.Complete();

        await outbox.RunAsync(CancellationToken.None);

        Assert.Equal(2, poster.Posts.Count);
        Assert.Same(poster.Posts[0], poster.Posts[1]);
        Assert.Contains(TimeSpan.FromSeconds(3), _waits);
    }

    [Fact]
    public async Task RunAsync_BucketEmptyAfterASend_WaitsBeforeTheNextPost()
    {
        var poster = new FakePoster(new DiscordPostResult(DiscordPostOutcome.Sent, TimeSpan.FromSeconds(2)));
        var outbox = NewOutbox(poster);
        outbox.Enqueue(new("one", "Steve"));
        outbox.Enqueue(new("two", "Alex"));
        outbox.Complete();

        await outbox.RunAsync(CancellationToken.None);

        Assert.Equal(2, poster.Posts.Count);
        Assert.Equal([DiscordOutbox.BatchWindow, TimeSpan.FromSeconds(2)], _waits);
    }

    [Fact]
    public async Task RunAsync_Rejected_DropsThePostWithoutRetrying()
    {
        var poster = new FakePoster(new DiscordPostResult(DiscordPostOutcome.Rejected, StatusCode: 404));
        var log = new StringWriter();
        var outbox = new DiscordOutbox(poster, new BridgeLog(log), RecordWait);
        outbox.Enqueue(new("hello"));
        outbox.Complete();

        await outbox.RunAsync(CancellationToken.None);

        Assert.Single(poster.Posts);
        Assert.Contains("HTTP 404", log.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_DiscordKeepsFailing_GivesUpAfterMaxAttempts()
    {
        var failed = new DiscordPostResult(DiscordPostOutcome.Failed);
        var poster = new FakePoster(Enumerable.Repeat(failed, 10).ToArray());
        var outbox = NewOutbox(poster);
        outbox.Enqueue(new("hello"));
        outbox.Complete();

        await outbox.RunAsync(CancellationToken.None);

        Assert.Equal(DiscordOutbox.MaxAttempts, poster.Posts.Count);
    }

    [Fact]
    public async Task Enqueue_QueueFull_DropsTheOldestLines()
    {
        var poster = new FakePoster();
        var outbox = NewOutbox(poster);
        for (var i = 0; i < DiscordOutbox.Capacity + 5; i++)
        {
            outbox.Enqueue(new($"line {i}", $"p{i}"));
        }

        outbox.Complete();

        await outbox.RunAsync(CancellationToken.None);

        // Every round is a flood of different players, so the lines are merged across them: nothing is sent per line.
        var lines = poster.Posts.SelectMany(post => post.Content!.Split('\n')).ToList();
        Assert.Equal(DiscordOutbox.Capacity, lines.Count);
        Assert.Equal("**p5**: line 5", lines[0]);
        Assert.True(poster.Posts.Count <= DiscordOutbox.Capacity / DiscordOutbox.MaxMessagesPerRound * DiscordOutbox.MaxPostsPerRound);
    }

    [Fact]
    public async Task RunAsync_FewPlayersTalking_KeepsEachUnderTheirOwnNameAndPicture()
    {
        var poster = new FakePoster();
        var outbox = NewOutbox(poster);
        outbox.Enqueue(new("hi", "Steve", "https://mc-heads.net/avatar/Steve/64"));
        outbox.Enqueue(new("hello", "Alex", "https://mc-heads.net/avatar/Alex/64"));
        outbox.Complete();

        await outbox.RunAsync(CancellationToken.None);

        Assert.Equal(["Steve", "Alex"], poster.Posts.Select(p => p.Username));
    }

    private DiscordOutbox NewOutbox(IDiscordPoster poster) => new(poster, new BridgeLog(TextWriter.Null), RecordWait);

    private Task RecordWait(TimeSpan wait, CancellationToken cancellationToken)
    {
        _waits.Add(wait);
        return Task.CompletedTask;
    }
}
