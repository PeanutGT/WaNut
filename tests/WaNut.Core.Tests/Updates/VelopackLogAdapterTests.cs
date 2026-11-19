using Microsoft.Extensions.Time.Testing;
using Velopack.Logging;
using WaNut.Core.Tests.TestSupport;
using WaNut.Core.Updates;

namespace WaNut.Core.Tests.Updates;

public sealed class VelopackLogAdapterTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly UpdateLog _log;

    public VelopackLogAdapterTests()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero));
        time.SetLocalTimeZone(TimeZoneInfo.Utc);
        _log = new UpdateLog(new LauncherPaths(_dir.Path), time);
    }

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Writes_messages_and_errors_but_not_trace()
    {
        var adapter = new VelopackLogAdapter(_log);

        adapter.Log(VelopackLogLevel.Trace, "noise", null);
        adapter.Log(VelopackLogLevel.Error, "Unable to move the app folder", new IOException("in use"));

        var text = File.ReadAllText(_log.FilePath);
        Assert.DoesNotContain("noise", text);
        Assert.Contains("Velopack Error: Unable to move the app folder (IOException: in use)", text);
    }
}
