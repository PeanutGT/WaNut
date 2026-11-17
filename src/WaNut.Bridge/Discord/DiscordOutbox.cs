using System.Threading.Channels;

namespace WaNut.Bridge.Discord;

/// <summary>
/// The queue between the game and Discord. Lines that arrive within <see cref="BatchWindow"/> of each other go out as
/// one post, and while Discord asks the bridge to wait, new lines pile up and are merged into the next post instead
/// of being sent one by one. When the queue is full (Discord down for a long time) the oldest lines are dropped.
/// </summary>
public sealed class DiscordOutbox
{
    public const int Capacity = 500;

    public const int MaxAttempts = 5;

    public const int MaxMessagesPerRound = 100;

    /// <summary>
    /// More posts than this in one round is a flood (about what a rate-limit window allows, 5 per 2 seconds, over
    /// the 300 ms batch window); the round is then merged across players so no line has to wait for its own request.
    /// </summary>
    public const int MaxPostsPerRound = 5;

    public static readonly TimeSpan BatchWindow = TimeSpan.FromMilliseconds(300);

    private static readonly TimeSpan FailureWait = TimeSpan.FromSeconds(2);

    private readonly IDiscordPoster _poster;
    private readonly BridgeLog _log;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Channel<OutgoingMessage> _queue;
    private TimeSpan _bucketWait;
    private int _dropped;

    public DiscordOutbox(IDiscordPoster poster, BridgeLog log, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _poster = poster;
        _log = log;
        _delay = delay ?? Task.Delay;
        _queue = Channel.CreateBounded<OutgoingMessage>(
            new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true },
            _ => OnDropped());
    }

    public void Enqueue(OutgoingMessage message) => _queue.Writer.TryWrite(message);

    /// <summary>No more lines will come; <see cref="RunAsync"/> ends once the queue is empty.</summary>
    public void Complete() => _queue.Writer.TryComplete();

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var reader = _queue.Reader;
        while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            await _delay(BatchWindow, cancellationToken).ConfigureAwait(false);
            var batch = new List<OutgoingMessage>();
            while (batch.Count < MaxMessagesPerRound && reader.TryRead(out var message))
            {
                batch.Add(message);
            }

            var posts = DiscordBatcher.Merge(batch);
            if (posts.Count > MaxPostsPerRound)
            {
                posts = DiscordBatcher.Merge(batch, collapseSenders: true);
            }

            foreach (var post in posts)
            {
                await SendAsync(post, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task SendAsync(DiscordPost post, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            if (_bucketWait > TimeSpan.Zero)
            {
                await _delay(_bucketWait, cancellationToken).ConfigureAwait(false);
                _bucketWait = TimeSpan.Zero;
            }

            var result = await _poster.PostAsync(post, cancellationToken).ConfigureAwait(false);
            switch (result.Outcome)
            {
                case DiscordPostOutcome.Sent:
                    _bucketWait = result.Wait;
                    return;
                case DiscordPostOutcome.Rejected:
                    _log.Error($"Discord refused a message (HTTP {result.StatusCode}). Check the webhook URL, bot token and channel permissions.");
                    return;
                case DiscordPostOutcome.RateLimited:
                    await _delay(result.Wait, cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    await _delay(FailureWait * attempt, cancellationToken).ConfigureAwait(false);
                    break;
            }
        }

        _log.Warn($"Gave up on a Discord message after {MaxAttempts} tries.");
    }

    private void OnDropped()
    {
        var dropped = Interlocked.Increment(ref _dropped);
        if (dropped == 1 || dropped % 100 == 0)
        {
            _log.Warn($"Discord is not keeping up; {dropped} old messages were dropped so far.");
        }
    }
}
