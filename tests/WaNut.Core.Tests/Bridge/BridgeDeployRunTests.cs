using WaNut.Core.Bridge;

namespace WaNut.Core.Tests.Bridge;

public sealed class BridgeDeployRunTests
{
    [Fact]
    public async Task RunAsync_FailureLater_UndoesNewestFirstAndReportsTheStep()
    {
        var undone = new List<int>();

        var error = await Assert.ThrowsAsync<BridgeDeployException>(() => BridgeDeployRun.RunAsync<int>(null, run =>
        {
            run.Enter(BridgeDeployStep.PatchServerProperties);
            run.OnFailureSync(() => undone.Add(1));
            run.Enter(BridgeDeployStep.Register);
            run.OnFailureSync(() => undone.Add(2));
            throw new IOException("disk full");
        }));

        Assert.Equal([2, 1], undone);
        Assert.Equal(BridgeDeployStep.Register, error.Step);
        Assert.Equal(BridgeDeployError.RegisterFailed, error.Error);
        Assert.Equal("disk full", error.Detail);
    }

    [Fact]
    public async Task RunAsync_UndoThatFails_StillRunsTheRest()
    {
        var undone = false;

        await Assert.ThrowsAsync<BridgeDeployException>(() => BridgeDeployRun.RunAsync<int>(null, run =>
        {
            run.OnFailureSync(() => undone = true);
            run.OnFailureSync(() => throw new IOException());
            throw run.Fail(BridgeDeployError.InvalidRequest);
        }));

        Assert.True(undone);
    }

    [Fact]
    public async Task RunAsync_Success_ReportsEveryStepAndUndoesNothing()
    {
        var steps = new List<BridgeDeployStep>();
        var undone = false;

        var result = await BridgeDeployRun.RunAsync(new SyncProgress(steps.Add), run =>
        {
            run.Enter(BridgeDeployStep.Validate);
            run.OnFailureSync(() => undone = true);
            run.Enter(BridgeDeployStep.HealthCheck);
            return Task.FromResult(7);
        });

        Assert.Equal(7, result);
        Assert.False(undone);
        Assert.Equal([BridgeDeployStep.Validate, BridgeDeployStep.HealthCheck], steps);
    }

    private sealed class SyncProgress(Action<BridgeDeployStep> report) : IProgress<BridgeDeployStep>
    {
        public void Report(BridgeDeployStep value) => report(value);
    }
}
