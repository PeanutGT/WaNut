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
        foreach (var id in new[] { "first", "second" })
        {
            if (_host.Get(id) is { } server)
            {
                if (server.IsRunning)
                {
                    server.Kill();
                    server.Exited.Wait(5000);
                }

                server.Dispose();
            }
        }

        _temp.Dispose();
    }

    [Fact]
    public async Task Start_SecondServerWhileFirstRuns_RunsBoth()
    {
        var first = _host.Start("first", FakeServer.Launch(_temp.Path));
        await FakeServer.WaitUntilAsync(() => first.Status == ServerStatus.Online);

        var second = _host.Start("second", FakeServer.Launch(_temp.Path));
        await FakeServer.WaitUntilAsync(() => second.Status == ServerStatus.Online);

        Assert.Equal(["first", "second"], _host.RunningInstanceIds.Order());
        Assert.Same(second, _host.Get("second"));
    }

    [Fact]
    public async Task Start_SameServerWhileItRuns_ThrowsAlreadyRunning()
    {
        var first = _host.Start("first", FakeServer.Launch(_temp.Path));
        await FakeServer.WaitUntilAsync(() => first.Status == ServerStatus.Online);

        var error = Assert.Throws<ServerStartException>(() => _host.Start("first", FakeServer.Launch(_temp.Path)));

        Assert.Equal(ServerStartProblem.AlreadyRunning, error.Problem);
        Assert.Same(first, _host.Get("first"));
    }

    [Fact]
    public async Task Start_AfterSameServerStopped_StartsNewProcess()
    {
        var first = _host.Start("first", FakeServer.Launch(_temp.Path));
        await FakeServer.WaitUntilAsync(() => first.Status == ServerStatus.Online);
        await first.StopAsync(TimeSpan.FromSeconds(15));

        var again = _host.Start("first", FakeServer.Launch(_temp.Path));

        Assert.Same(again, _host.Get("first"));
        Assert.Equal(["first"], _host.RunningInstanceIds);
    }

    [Fact]
    public async Task Start_BeforeStartCallback_ReceivesFirstLine()
    {
        var lines = new ConcurrentQueue<string>();

        var server = _host.Start("first", FakeServer.Launch(_temp.Path), p => p.OutputReceived += lines.Enqueue);
        await FakeServer.WaitUntilAsync(() => server.Status == ServerStatus.Online);

        // stdout and stderr are read on separate threads, so only the first stdout line is checked.
        Assert.EndsWith("Starting minecraft server version fake", lines.First(l => l.Contains("]: ", StringComparison.Ordinal)), StringComparison.Ordinal);
    }

    [Fact]
    public void RunningInstanceIds_NothingStarted_IsEmpty()
    {
        Assert.Empty(_host.RunningInstanceIds);
    }
}
