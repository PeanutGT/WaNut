using WaNut.Bridge.Discord;

namespace WaNut.Bridge.Tests.Discord;

/// <summary>Answers each post with the next queued result (Sent once the queue is empty) and records what it got.</summary>
internal sealed class FakePoster(params DiscordPostResult[] results) : IDiscordPoster
{
    private readonly Queue<DiscordPostResult> _results = new(results);

    public List<DiscordPost> Posts { get; } = [];

    public Task<DiscordPostResult> PostAsync(DiscordPost post, CancellationToken cancellationToken)
    {
        Posts.Add(post);
        return Task.FromResult(_results.TryDequeue(out var result) ? result : new DiscordPostResult(DiscordPostOutcome.Sent));
    }
}
