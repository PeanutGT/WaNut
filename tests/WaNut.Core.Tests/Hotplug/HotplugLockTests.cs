using WaNut.Core.Hotplug;
using WaNut.Core.Remote.Files;

namespace WaNut.Core.Tests.Hotplug;

public sealed class HotplugLockTests
{
    private static readonly HotplugWorkspace Workspace = HotplugWorkspaceInitializer.Plan("world", sameMount: true);

    [Fact]
    public async Task TryAcquireAsync_Free_ReturnsLock()
    {
        var runner = new FakeRunner();

        var result = await HotplugLock.TryAcquireAsync(runner, "/srv", Workspace);

        Assert.True(result.Acquired);
        Assert.Equal(".nut-staging/.lock", result.Lock!.LockFile);
        Assert.Contains($"pid={Environment.ProcessId}", runner.Commands[0]);
    }

    [Fact]
    public async Task TryAcquireAsync_Held_ReportsHolder()
    {
        var runner = new FakeRunner(new RemoteCommandResult(HotplugCommands.LockHeldExit, "pid=42\nmachine=Anna-PC\nowner=x\ntime=1700000000\n", string.Empty));

        var result = await HotplugLock.TryAcquireAsync(runner, "/srv", Workspace);

        Assert.False(result.Acquired);
        Assert.Equal(new HotplugLockHolder(42, "Anna-PC", DateTimeOffset.FromUnixTimeSeconds(1700000000)), result.Holder);
    }

    [Fact]
    public async Task TryAcquireAsync_OtherFailure_Throws() =>
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            HotplugLock.TryAcquireAsync(new FakeRunner(new RemoteCommandResult(1, string.Empty, "boom")), "/srv", Workspace));

    [Fact]
    public async Task DisposeAsync_ReleasesOnce()
    {
        var runner = new FakeRunner();
        var held = (await HotplugLock.TryAcquireAsync(runner, "/srv", Workspace)).Lock!;

        await held.DisposeAsync();
        await held.DisposeAsync();

        Assert.Equal(2, runner.Commands.Count);
    }

    [Fact]
    public async Task RefreshAsync_TakenOver_ReturnsFalse()
    {
        var runner = new FakeRunner(new RemoteCommandResult(0, string.Empty, string.Empty), new RemoteCommandResult(HotplugCommands.LockHeldExit, string.Empty, string.Empty));
        var held = (await HotplugLock.TryAcquireAsync(runner, "/srv", Workspace)).Lock!;

        Assert.False(await held.RefreshAsync());
    }

    [Fact]
    public void Parse_Garbage_LeavesFieldsNull() =>
        Assert.Equal(new HotplugLockHolder(null, null, null), HotplugLockHolder.Parse("nonsense\npid=abc\n"));
}
