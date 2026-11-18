using Renci.SshNet.Common;
using WaNut.Core.Remote.Files;

namespace WaNut.Core.Tests.Remote.Files;

public sealed class VpsSessionTests
{
    [Fact]
    public async Task RetryOnceAsync_ConnectionLost_DropsClientAndRetriesWithFreshOne()
    {
        var made = 0;
        var dropped = new List<int>();

        var result = await VpsSession.RetryOnceAsync(
            () => Task.FromResult(++made),
            client => client == 1 ? throw new SshConnectionException("gone") : Task.FromResult($"ran on {client}"),
            dropped.Add);

        Assert.Equal("ran on 2", result);
        Assert.Equal([1], dropped);
    }

    [Fact]
    public async Task RetryOnceAsync_LostTwice_GivesUp()
    {
        var calls = 0;

        await Assert.ThrowsAsync<SshConnectionException>(() => VpsSession.RetryOnceAsync<int, int>(
            () => Task.FromResult(0),
            _ =>
            {
                calls++;
                throw new SshConnectionException("gone");
            },
            _ => { }));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task RetryOnceAsync_OtherFailure_NotRetried()
    {
        var calls = 0;

        await Assert.ThrowsAsync<IOException>(() => VpsSession.RetryOnceAsync<int, int>(
            () => Task.FromResult(0),
            _ =>
            {
                calls++;
                throw new IOException("disk full");
            },
            _ => { }));
        Assert.Equal(1, calls);
    }
}
