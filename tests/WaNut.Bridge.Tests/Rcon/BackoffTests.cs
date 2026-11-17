using WaNut.Bridge.Rcon;

namespace WaNut.Bridge.Tests.Rcon;

public sealed class BackoffTests
{
    [Fact]
    public void Next_RepeatedFailures_DoublesUpToTheCap()
    {
        var backoff = new Backoff(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10));

        var waits = Enumerable.Range(0, 6).Select(_ => backoff.Next().TotalSeconds);

        Assert.Equal([1, 2, 4, 8, 10, 10], waits);
    }

    [Fact]
    public void Next_AfterReset_StartsOver()
    {
        var backoff = new Backoff(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(60));
        backoff.Next();
        backoff.Next();

        backoff.Reset();

        Assert.Equal(TimeSpan.FromSeconds(1), backoff.Next());
    }
}
