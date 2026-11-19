using Microsoft.Extensions.Time.Testing;
using WaNut.Core.Hotplug.Safety;
using static WaNut.Core.Tests.Hotplug.Safety.ScriptedConsole;

namespace WaNut.Core.Tests.Hotplug.Safety;

public sealed class LoadReloadGateTests
{
    private readonly FakeTimeProvider _time = new();
    private int _players = 5;
    private double _mspt = 60;

    private ScriptedConsole Console() => new(c => c switch
    {
        "list" => [Info($"There are {_players} of a max of 20 players online: x")],
        "mspt" => [Info(FormattableString.Invariant($"◴ {_mspt:0.0}/1.0/90.0, 1.0/1.0/1.0, 1.0/1.0/1.0"))],
        _ => [],
    });

    private LoadReloadGate Gate(LoadChoice choice, List<ServerLoad>? asked = null, List<ServerLoad>? queued = null) =>
        new((load, _) =>
        {
            asked?.Add(load);
            return Task.FromResult(choice);
        }, queued is null ? null : queued.Add, _time)
        { QueryTimeout = TimeSpan.FromMilliseconds(300) };

    [Fact]
    public async Task CheckAsync_QuietServer_GoesAheadWithoutAsking()
    {
        _mspt = 20;
        var asked = new List<ServerLoad>();

        Assert.Null(await Gate(LoadChoice.Cancel, asked).CheckAsync(Console(), CancellationToken.None));
        Assert.Empty(asked);
    }

    [Fact]
    public async Task CheckAsync_UnknownTickTime_GoesAhead()
    {
        var console = new ScriptedConsole(c => c == "list" ? [Info("There are 9 of a max of 20 players online: x")] : []);

        Assert.Null(await Gate(LoadChoice.Cancel).CheckAsync(console, CancellationToken.None));
    }

    [Fact]
    public async Task CheckAsync_BusyAndRunNow_GoesAhead()
    {
        var asked = new List<ServerLoad>();

        Assert.Null(await Gate(LoadChoice.RunNow, asked).CheckAsync(Console(), CancellationToken.None));
        Assert.Equal([new ServerLoad(5, 60, "mspt")], asked);
    }

    [Fact]
    public async Task CheckAsync_BusyAndCancel_ReturnsReason()
    {
        var reason = await Gate(LoadChoice.Cancel).CheckAsync(Console(), CancellationToken.None);

        Assert.Contains("60 ms per tick", reason);
    }

    [Fact]
    public async Task CheckAsync_Queued_RunsOncePlayersLeave()
    {
        var queued = new List<ServerLoad>();
        var check = Gate(LoadChoice.Queue, queued: queued).CheckAsync(Console(), CancellationToken.None);
        await WaitUntilAsync(() => queued.Count == 1);

        _players = 3;
        await AdvanceUntilAsync(() => queued.Count >= 2);
        Assert.False(check.IsCompleted);

        _players = 1;
        await AdvanceUntilAsync(() => check.IsCompleted);

        Assert.Null(await check);
    }

    [Fact]
    public async Task CheckAsync_QueuedThenCancelled_Throws()
    {
        using var cts = new CancellationTokenSource();
        var queued = new List<ServerLoad>();
        var check = Gate(LoadChoice.Queue, queued: queued).CheckAsync(Console(), cts.Token);
        await WaitUntilAsync(() => queued.Count == 1);

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => check);
    }

    /// <summary>Moves the clock one poll at a time; the gate sets its timer on another thread, so once may come too early.</summary>
    private async Task AdvanceUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            _time.Advance(LoadReloadGate.DefaultPollInterval);
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }
}
