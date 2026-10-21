using System.Collections.Concurrent;
using WaNut.Core.Hosting;
using WaNut.Core.Tests.TestSupport;

namespace WaNut.Core.Tests.Hosting;

public sealed class ServerProcessTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly List<ServerProcess> _started = [];

    public void Dispose()
    {
        foreach (var server in _started)
        {
            server.Kill();
            server.Exited.Wait(5000);
            server.Dispose();
        }

        _temp.Dispose();
    }

    private ServerProcess StartFake(params string[] extraArgs)
    {
        var server = new ServerProcess(FakeServer.Launch(_temp.Path, extraArgs));
        _started.Add(server);
        server.Start();
        return server;
    }

    [Fact]
    public async Task Start_DoneLine_GoesFromStartingToOnline()
    {
        var statuses = new ConcurrentQueue<ServerStatus>();
        var server = new ServerProcess(FakeServer.Launch(_temp.Path));
        _started.Add(server);
        server.StatusChanged += statuses.Enqueue;

        server.Start();
        await FakeServer.WaitUntilAsync(() => server.Status == ServerStatus.Online);

        Assert.Equal([ServerStatus.Starting, ServerStatus.Online], statuses);
    }

    [Fact]
    public async Task Start_Output_StreamsStdoutAndStderrLines()
    {
        var lines = new ConcurrentQueue<string>();
        var server = new ServerProcess(FakeServer.Launch(_temp.Path));
        _started.Add(server);
        server.OutputReceived += lines.Enqueue;

        server.Start();
        await FakeServer.WaitUntilAsync(() => server.Status == ServerStatus.Online);
        await FakeServer.WaitUntilAsync(() => lines.Contains("This line goes to stderr"));

        Assert.Contains(lines, l => l.EndsWith("Preparing level \"world\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SendCommandAsync_JoinAndLeave_UpdatesPlayers()
    {
        var server = StartFake();
        await FakeServer.WaitUntilAsync(() => server.Status == ServerStatus.Online);

        await server.SendCommandAsync("join Steve");
        await server.SendCommandAsync("join Alex");
        await FakeServer.WaitUntilAsync(() => server.Players.Count == 2);
        await server.SendCommandAsync("leave Steve");
        await FakeServer.WaitUntilAsync(() => server.Players.Count == 1);

        Assert.Equal(["Alex"], server.Players);
    }

    [Fact]
    public async Task SendCommandAsync_NonAsciiText_RoundTripsAsUtf8()
    {
        var lines = new ConcurrentQueue<string>();
        var server = StartFake();
        server.OutputReceived += lines.Enqueue;
        await FakeServer.WaitUntilAsync(() => server.Status == ServerStatus.Online);

        await server.SendCommandAsync("say 你好 ñ");

        await FakeServer.WaitUntilAsync(() => lines.Any(l => l.EndsWith("[Server] 你好 ñ", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task SendCommandAsync_TextWithNewline_SendsOneLine()
    {
        var lines = new ConcurrentQueue<string>();
        var server = StartFake();
        server.OutputReceived += lines.Enqueue;
        await FakeServer.WaitUntilAsync(() => server.Status == ServerStatus.Online);

        await server.SendCommandAsync("say hello\nstop");

        await FakeServer.WaitUntilAsync(() => lines.Any(l => l.EndsWith("[Server] hello stop", StringComparison.Ordinal)));
        Assert.Equal(ServerStatus.Online, server.Status);
    }

    [Fact]
    public async Task StopAsync_RunningServer_ExitsCleanlyAndClearsPlayers()
    {
        var server = StartFake();
        await FakeServer.WaitUntilAsync(() => server.Status == ServerStatus.Online);
        await server.SendCommandAsync("join Steve");
        await FakeServer.WaitUntilAsync(() => server.Players.Count == 1);

        var stopped = await server.StopAsync(TimeSpan.FromSeconds(15));

        Assert.True(stopped);
        Assert.Equal(ServerStatus.Stopped, server.Status);
        Assert.Equal(0, await server.Exited);
        Assert.Empty(server.Players);
        Assert.True(server.StopRequested);
    }

    [Fact]
    public async Task SendCommandAsync_OtherCommand_DoesNotCountAsStop()
    {
        var server = StartFake();
        await FakeServer.WaitUntilAsync(() => server.Status == ServerStatus.Online);

        await server.SendCommandAsync("say stop");

        Assert.False(server.StopRequested);
        await server.StopAsync(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task StopAsync_ServerIgnoresStop_ReturnsFalseAndKeepsRunning()
    {
        var server = StartFake("--ignore-stop");
        await FakeServer.WaitUntilAsync(() => server.Status == ServerStatus.Online);

        var stopped = await server.StopAsync(TimeSpan.FromMilliseconds(500));

        Assert.False(stopped);
        Assert.Equal(ServerStatus.Stopping, server.Status);
        Assert.True(server.IsRunning);
    }

    [Fact]
    public async Task Kill_AfterStopTimedOut_EndsProcess()
    {
        var server = StartFake("--ignore-stop");
        await FakeServer.WaitUntilAsync(() => server.Status == ServerStatus.Online);
        await server.StopAsync(TimeSpan.FromMilliseconds(200));

        server.Kill();
        await server.Exited.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Equal(ServerStatus.Stopped, server.Status);
    }

    [Fact]
    public async Task Exited_ServerCrashes_ReportsExitCodeAndStopped()
    {
        var server = StartFake();
        await FakeServer.WaitUntilAsync(() => server.Status == ServerStatus.Online);

        await server.SendCommandAsync("crash");

        Assert.Equal(3, await server.Exited.WaitAsync(TimeSpan.FromSeconds(15)));
        Assert.Equal(ServerStatus.Stopped, server.Status);
    }

    [Fact]
    public async Task SendCommandAsync_AfterExit_Throws()
    {
        var server = StartFake();
        await FakeServer.WaitUntilAsync(() => server.Status == ServerStatus.Online);
        await server.StopAsync(TimeSpan.FromSeconds(15));

        await Assert.ThrowsAsync<InvalidOperationException>(() => server.SendCommandAsync("say hi"));
    }

    [Fact]
    public void Start_MissingProgram_ThrowsAndStaysStopped()
    {
        var server = new ServerProcess(new ServerLaunch(Path.Combine(_temp.Path, "missing.exe"), [], _temp.Path));

        Assert.ThrowsAny<Exception>(server.Start);
        Assert.Equal(ServerStatus.Stopped, server.Status);
    }

    [Fact]
    public async Task Start_AnyServer_IsTrackedByJobObjectOnWindowsOnly()
    {
        var server = StartFake();
        await FakeServer.WaitUntilAsync(() => server.Status == ServerStatus.Online);

        Assert.Equal(OperatingSystem.IsWindows(), server.IsTrackedByJob);
    }
}
