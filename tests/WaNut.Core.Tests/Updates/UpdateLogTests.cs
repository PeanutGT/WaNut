using Microsoft.Extensions.Time.Testing;
using WaNut.Core.Tests.TestSupport;
using WaNut.Core.Updates;

namespace WaNut.Core.Tests.Updates;

public sealed class UpdateLogTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 18, 9, 30, 5, TimeSpan.Zero));

    public UpdateLogTests() => _time.SetLocalTimeZone(TimeZoneInfo.Utc);

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Write_AddsTimestampedLineInDataFolder()
    {
        var log = new UpdateLog(new LauncherPaths(_dir.Path), _time);

        log.Write("Startup check: up to date");

        Assert.Equal(Path.Combine(_dir.Path, "update.log"), log.FilePath);
        Assert.Equal(["2026-10-18 09:30:05 Startup check: up to date"], File.ReadAllLines(log.FilePath));
    }

    [Fact]
    public void Write_KeepsMessageOnOneLine()
    {
        var log = new UpdateLog(new LauncherPaths(_dir.Path), _time);

        log.Write("failed:\nno network");

        Assert.Single(File.ReadAllLines(log.FilePath));
    }

    [Fact]
    public void Write_PastLimit_DropsOldestLines()
    {
        var log = new UpdateLog(new LauncherPaths(_dir.Path), _time);

        for (var i = 0; i < UpdateLog.MaxLines + 5; i++)
        {
            log.Write($"line {i}");
        }

        var lines = File.ReadAllLines(log.FilePath);
        Assert.Equal(UpdateLog.MaxLines, lines.Length);
        Assert.EndsWith("line 5", lines[0]);
        Assert.EndsWith($"line {UpdateLog.MaxLines + 4}", lines[^1]);
    }
}
