using WaNut.Core.Bridge;

namespace WaNut.Bridge.Tests;

public sealed class StatusPipeServerTests
{
    [Fact]
    public async Task AskAsync_RunningServer_AnswersStatusThenStops()
    {
        var id = "test-" + Guid.NewGuid().ToString("N");
        using var stop = new CancellationTokenSource();
        var server = new StatusPipeServer(id, () => new BridgeStatus { RconConnected = true, ProcessId = 7 }, stop.Cancel, new BridgeLog(TextWriter.Null));
        var running = server.RunAsync(stop.Token);

        var status = await BridgeStatusPipe.AskAsync(id, timeout: TimeSpan.FromSeconds(5));
        var stopped = await BridgeStatusPipe.AskAsync(id, BridgeStatusPipe.StopRequest, TimeSpan.FromSeconds(5));

        Assert.Equal(BridgeState.Running, status?.State);
        Assert.True(status?.RconConnected);
        Assert.Equal(BridgeState.Stopped, stopped?.State);
        // The answer goes out before the bridge acts on it.
        Assert.True(SpinWait.SpinUntil(() => stop.IsCancellationRequested, TimeSpan.FromSeconds(5)));
        var ended = await Record.ExceptionAsync(() => running.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(ended is null or OperationCanceledException);
    }
}
