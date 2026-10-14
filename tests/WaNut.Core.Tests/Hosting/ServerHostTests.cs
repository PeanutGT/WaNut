using System.Collections.Concurrent;
using WaNut.Core.Hosting;
using WaNut.Core.Tests.TestSupport;

namespace WaNut.Core.Tests.Hosting;

public sealed class ServerHostTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ServerHost _host = new();

    public void Dispose()
    {
        if (_host.Current is { } server)
        {
            server.Kill();
            server.Exited.Wait(5000);
            server.Dispose();
        }

        _temp.Dispose();
    }

    [Fact]
    public async Task Start_WhileAnotherRuns_ThrowsAnotherServerRunning()
    {
        var first = _host.Start("first", FakeServer.Launch(_temp.Path));
        await FakeServer.WaitUntilAsync(() => first.Status == ServerStatus.Online);

        var error = Assert.Throws<ServerStartException>(() => _host.Start("second", FakeServer.Launch(_temp.Path)));

        Assert.Equal(ServerStartProblem.AnotherServerRunning, error.Problem);
        Assert.Equal("first", _host.RunningInstanceId);
    }

    [Fact]
    public async Task Start_AfterPreviousStopped_StartsNewServer()
    {
        var first = _host.Start("first", FakeServer.Launch(_temp.Path));
        await FakeServer.WaitUntilAsync(() => first.Status == ServerStatus.Online);
        await first.StopAsync(TimeSpan.FromSeconds(15));

        var second = _host.Start("second", FakeServer.Launch(_temp.Path));

        Assert.Same(second, _host.Current);
        Assert.Equal("second", _host.RunningInstanceId);
    }

    [Fact]
    public async Task Start_BeforeStartCallback_ReceivesFirstLine()
    {
        var lines = new ConcurrentQueue<string>();

        var server = _host.Start("first", FakeServer.Launch(_temp.Path), p => p.OutputReceived += lines.Enqueue);
        await FakeServer.WaitUntilAsync(() => server.Status == ServerStatus.Online);

        Assert.EndsWith("Starting minecraft server version fake", lines.First(), StringComparison.Ordinal);
    }

    [Fact]
    public void RunningInstanceId_NothingStarted_IsNull()
    {
        Assert.Null(_host.RunningInstanceId);
    }
}
