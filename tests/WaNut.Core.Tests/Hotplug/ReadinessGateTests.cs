using WaNut.Core.Hotplug.Readiness;
using WaNut.Core.Tests.TestSupport;

namespace WaNut.Core.Tests.Hotplug;

public sealed class ReadinessGateTests
{
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    private ReadinessGate Gate() => new(_clock, (span, _) =>
    {
        _clock.Advance(span);
        return Task.CompletedTask;
    });

    private static ReadinessOptions Defaults(string? sentinel = null, TimeSpan? timeout = null) =>
        new() { SentinelName = sentinel, Timeout = timeout ?? TimeSpan.FromMinutes(5) };

    [Fact]
    public async Task WaitAsync_StableFile_ReadyAfterDebounce()
    {
        var inbox = new FakeInbox(_clock).Put("a.zip", 100);

        var result = await Gate().WaitAsync(inbox, Defaults());

        Assert.True(result.IsReady);
        Assert.Equal(["a.zip"], result.Files.Select(f => f.Name));
        Assert.True(_clock.Now - new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) >= TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task WaitAsync_FileStillGrowing_WaitsUntilItStops()
    {
        var inbox = new FakeInbox(_clock).Put("a.zip", 10)
            .At(1, i => i.Put("a.zip", 20))
            .At(2, i => i.Put("a.zip", 30));

        var result = await Gate().WaitAsync(inbox, Defaults());

        Assert.True(result.IsReady);
        Assert.Equal(30, result.Files[0].SizeBytes);
        // Last change at 2 s, plus the 3 s window.
        Assert.True(_clock.Now >= new DateTimeOffset(2026, 1, 1, 0, 0, 5, TimeSpan.Zero));
    }

    [Fact]
    public async Task WaitAsync_ThreeFilesOneAndAHalfSecondsApart_ReadyOnceWithAllThree()
    {
        var inbox = new FakeInbox(_clock).Put("a.zip", 1)
            .At(1.5, i => i.Put("b.zip", 1))
            .At(3, i => i.Put("c.zip", 1));
        var statuses = new List<ReadinessStatus>();

        var result = await Gate().WaitAsync(inbox, Defaults(), new Progress(statuses));

        Assert.True(result.IsReady);
        Assert.Equal(["a.zip", "b.zip", "c.zip"], result.Files.Select(f => f.Name).Order());
        Assert.Contains(statuses, s => s.Phase == ReadinessPhase.Debouncing && s.DebounceRemaining > TimeSpan.Zero);
    }

    [Fact]
    public async Task WaitAsync_PartFiles_AreNotData()
    {
        var inbox = new FakeInbox(_clock).Put("a.zip.part", 5).Put(".hidden", 1).Put("a.zip", 10)
            .At(4, i => i.Remove("a.zip.part"));

        var result = await Gate().WaitAsync(inbox, Defaults());

        Assert.Equal(["a.zip"], result.Files.Select(f => f.Name));
    }

    [Fact]
    public async Task WaitAsync_FileHeldOpen_WaitsUntilReleased()
    {
        var inbox = new FakeInbox(_clock).Put("a.zip", 10).At(0, i => i.Locked.Add("a.zip")).At(8, i => i.Locked.Clear());

        var result = await Gate().WaitAsync(inbox, Defaults());

        Assert.True(result.IsReady);
        Assert.True(_clock.Now >= new DateTimeOffset(2026, 1, 1, 0, 0, 11, TimeSpan.Zero));
    }

    [Fact]
    public async Task WaitAsync_NothingArrives_TimesOut()
    {
        var result = await Gate().WaitAsync(new FakeInbox(_clock), Defaults(timeout: TimeSpan.FromSeconds(10)));

        Assert.Equal(ReadinessOutcome.TimedOut, result.Outcome);
        Assert.Empty(result.Files);
    }

    [Fact]
    public async Task WaitAsync_NeverStops_TimesOut()
    {
        var size = 0;
        var inbox = new FakeInbox(_clock).Put("a.zip", 0);
        for (var s = 1; s < 30; s++)
        {
            inbox.At(s, i => i.Put("a.zip", ++size));
        }

        var result = await Gate().WaitAsync(inbox, Defaults(timeout: TimeSpan.FromSeconds(20)));

        Assert.Equal(ReadinessOutcome.TimedOut, result.Outcome);
    }

    [Fact]
    public async Task WaitAsync_ReadyFlag_WaitsForTheFlag()
    {
        var inbox = new FakeInbox(_clock).Put("a.zip", 1).At(20, i => i.Put(".ready", 0));

        var result = await Gate().WaitAsync(inbox, Defaults(".ready"));

        Assert.True(result.IsReady);
        Assert.Equal(["a.zip"], result.Files.Select(f => f.Name));
        Assert.True(_clock.Now >= new DateTimeOffset(2026, 1, 1, 0, 0, 20, TimeSpan.Zero));
    }

    [Fact]
    public async Task WaitAsync_ReadyFlagNeverComes_TimesOutNamingIt()
    {
        var inbox = new FakeInbox(_clock).Put("a.zip", 1);

        var result = await Gate().WaitAsync(inbox, Defaults(".ready", TimeSpan.FromSeconds(30)));

        Assert.Equal(ReadinessOutcome.TimedOut, result.Outcome);
        Assert.Contains(".ready", result.Message);
    }

    [Fact]
    public async Task WaitAsync_Manifest_BatchIsTheListedFiles()
    {
        var inbox = new FakeInbox(_clock)
            .Put("manifest.json", 30, """{"files":["a.zip","b.zip"]}""")
            .Put("a.zip", 1).Put("stray.zip", 1)
            .At(6, i => i.Put("b.zip", 1));

        var result = await Gate().WaitAsync(inbox, Defaults("manifest.json"));

        Assert.True(result.IsReady);
        Assert.Equal(["a.zip", "b.zip"], result.Files.Select(f => f.Name).Order());
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"files":[]}""")]
    [InlineData("""{"files":["../x.zip"]}""")]
    [InlineData("""{"other":1}""")]
    public async Task WaitAsync_BadManifest_Invalid(string text)
    {
        var inbox = new FakeInbox(_clock).Put("manifest.json", 5, text).Put("a.zip", 1);

        var result = await Gate().WaitAsync(inbox, Defaults("manifest.json"));

        Assert.Equal(ReadinessOutcome.InvalidManifest, result.Outcome);
    }

    [Fact]
    public async Task WaitAsync_Cancelled_Throws()
    {
        using var cts = new CancellationTokenSource();
        var gate = new ReadinessGate(_clock, (_, token) =>
        {
            cts.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gate.WaitAsync(new FakeInbox(_clock), Defaults(), cancellationToken: cts.Token));
    }

    private sealed class Progress(List<ReadinessStatus> into) : IProgress<ReadinessStatus>
    {
        public void Report(ReadinessStatus value) => into.Add(value);
    }
}
