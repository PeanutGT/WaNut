using WaNut.Bridge.Discord;

namespace WaNut.Bridge.Tests.Discord;

public sealed class DiscordBatcherTests
{
    [Fact]
    public void Merge_LinesFromTheSameSender_BecomeOnePost()
    {
        var posts = DiscordBatcher.Merge([new("Steve joined"), new("Alex joined"), new("Amy joined")]);

        Assert.Equal("Steve joined\nAlex joined\nAmy joined", Assert.Single(posts).Content);
    }

    [Fact]
    public void Merge_DifferentWebhookSenders_KeepOrderAndSplit()
    {
        var posts = DiscordBatcher.Merge([new("hi", "Steve", "a"), new("again", "Steve", "a"), new("yo", "Alex", "b"), new("back", "Steve", "a")]);

        Assert.Equal(["hi\nagain", "yo", "back"], posts.Select(p => p.Content));
        Assert.Equal(["Steve", "Alex", "Steve"], posts.Select(p => p.Username));
    }

    [Fact]
    public void Merge_TextOverTheLimit_StartsANewPost()
    {
        var line = new string('a', 1500);

        var posts = DiscordBatcher.Merge([new(line), new(line)]);

        Assert.Equal(2, posts.Count);
        Assert.All(posts, p => Assert.True(p.Content!.Length <= DiscordBatcher.MaxContentLength));
    }

    [Fact]
    public void Merge_OneLineOverTheLimit_IsCut()
    {
        var post = Assert.Single(DiscordBatcher.Merge([new(new string('a', 5000))]));

        Assert.Equal(DiscordBatcher.MaxContentLength, post.Content!.Length);
        Assert.EndsWith("…", post.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Merge_Embeds_GroupTenPerPost()
    {
        var posts = DiscordBatcher.Merge(Enumerable.Range(0, 12).Select(i => new OutgoingMessage($"e{i}", EmbedColor: 1)));

        Assert.Equal([10, 2], posts.Select(p => p.Embeds!.Count));
        Assert.All(posts, p => Assert.Null(p.Content));
    }

    [Fact]
    public void Merge_TextBetweenEmbeds_KeepsOrder()
    {
        var posts = DiscordBatcher.Merge([new("a", EmbedColor: 1), new("reply"), new("b", EmbedColor: 1)]);

        Assert.Equal(3, posts.Count);
        Assert.Equal("reply", posts[1].Content);
    }

    [Fact]
    public void Merge_Nothing_ReturnsNoPosts()
    {
        Assert.Empty(DiscordBatcher.Merge([]));
    }
}
