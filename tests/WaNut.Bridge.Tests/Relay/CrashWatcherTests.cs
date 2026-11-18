using WaNut.Bridge.Relay;
using WaNut.Core.Crashes;

namespace WaNut.Bridge.Tests.Relay;

public sealed class CrashWatcherTests
{
    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private const string Prefix = "[12:00:00] [Server thread/INFO]: ";

    [Fact]
    public void OnLine_OrdinaryLines_AreNotACrash()
    {
        var watcher = new CrashWatcher();

        Assert.False(watcher.OnLine(Prefix + "Steve joined the game"));
        Assert.False(watcher.OnLine(Prefix + "<Steve> hello"));
    }

    [Fact]
    public void OnLine_SecondCrashLineWithinCooldown_StaysQuiet_ThenAlertsAgain()
    {
        var time = new ManualTime();
        var watcher = new CrashWatcher(time);

        Assert.True(watcher.OnLine(Prefix + "Encountered an unexpected exception"));
        Assert.False(watcher.OnLine(Prefix + "This crash report has been saved to: x"));
        time.Now += CrashWatcher.Cooldown + TimeSpan.FromSeconds(1);
        Assert.True(watcher.OnLine(Prefix + "Exception in server tick loop"));
    }

    [Fact]
    public void Diagnose_PortInUse_ReadsTheKeptLines()
    {
        var watcher = new CrashWatcher();
        watcher.OnLine(Prefix + "**** FAILED TO BIND TO PORT!");
        watcher.OnLine(Prefix + "The exception was: java.net.BindException: Address already in use");
        watcher.OnLine(Prefix + "Perhaps a server is already running on that port?");

        var diagnosis = watcher.Diagnose(Path.Combine(Path.GetTempPath(), "nowhere", "logs", "latest.log"), DateTime.UtcNow);

        Assert.Equal(CrashCause.PortInUse, diagnosis?.Cause);
    }

    [Fact]
    public void Alert_KnownCauseNamesTheFix_UnknownSaysSo_InBothLanguages()
    {
        var port = CrashDiagnosis.Of(CrashCause.PortInUse, "25565");

        Assert.Contains("25565", CrashTexts.Alert(port, chinese: false), StringComparison.Ordinal);
        Assert.Contains("連接埠 25565", CrashTexts.Alert(port, chinese: true), StringComparison.Ordinal);
        Assert.Contains("unknown", CrashTexts.Alert(null, chinese: false), StringComparison.Ordinal);
    }
}
