using Microsoft.Extensions.Time.Testing;
using WaNut.Core.Hotplug.Safety;

namespace WaNut.Core.Tests.Hotplug.Safety;

public sealed class ReloadLogSentinelTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly ScriptedConsole _console = new(_ => []);

    private ReloadLogSentinel Sentinel() => new(_time);

    [Fact]
    public async Task FailureAsync_QuietWindow_NullAfterEightSeconds()
    {
        using var watch = Sentinel().Begin(_console);
        _console.Print(ScriptedConsole.Info("Reloading!"));

        var failure = watch.FailureAsync(CancellationToken.None);
        _time.Advance(TimeSpan.FromSeconds(7.9));
        Assert.False(failure.IsCompleted);
        _time.Advance(TimeSpan.FromSeconds(0.1));

        Assert.Null(await failure);
        Assert.False(watch.HasFailed);
    }

    [Fact]
    public async Task FailureAsync_ErrorWithStackTrace_ReturnsLinesAfterGrace()
    {
        using var watch = Sentinel().Begin(_console);
        var failure = watch.FailureAsync(CancellationToken.None);
        _time.Advance(TimeSpan.FromSeconds(2));
        _console.Print(ScriptedConsole.Error("Couldn't parse element minecraft:loot_table/ev:chest - Not a JSON object"));
        _console.Print("\tat net.minecraft.Foo.bar(Foo.java:12)");
        _console.Print(ScriptedConsole.Info("Loaded 7 recipes"));
        _console.Print("\tat not.part.of.the.error");

        Assert.True(watch.HasFailed);
        Assert.False(failure.IsCompleted);
        _time.Advance(ReloadLogSentinel.DefaultGrace);
        var result = await failure;

        Assert.NotNull(result);
        Assert.Equal(2, result.Lines.Count);
        Assert.Contains("ev:chest", result.Summary);
        Assert.StartsWith("The server reported an error", result.Summary);
    }

    [Fact]
    public async Task FailureAsync_SeveralErrors_CountsThem()
    {
        using var watch = Sentinel().Begin(_console);
        _console.Print(ScriptedConsole.Error("Couldn't parse element ev:a"));
        _console.Print(ScriptedConsole.Error("Couldn't parse element ev:b"));
        _time.Advance(TimeSpan.FromSeconds(9));

        var result = await watch.FailureAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains("2 errors", result.Summary);
    }

    [Fact]
    public async Task FailureAsync_LineAfterDispose_Ignored()
    {
        var watch = Sentinel().Begin(_console);
        watch.Dispose();
        _console.Print(ScriptedConsole.Error("Couldn't parse element ev:a"));
        _time.Advance(TimeSpan.FromSeconds(9));

        Assert.Null(await watch.FailureAsync(CancellationToken.None));
    }

    [Fact]
    public async Task FailureAsync_ManyLines_KeepsAtMostTwenty()
    {
        using var watch = Sentinel().Begin(_console);
        for (var i = 0; i < 50; i++)
        {
            _console.Print(ScriptedConsole.Error($"Couldn't parse element ev:{i}"));
        }

        _time.Advance(TimeSpan.FromSeconds(9));
        var result = await watch.FailureAsync(CancellationToken.None);

        Assert.Equal(ReloadLogSentinel.MaxLines, result!.Lines.Count);
        Assert.Contains("50 errors", result.Summary);
    }
}
