using WaNut.Core.Backups;
using WaNut.Core.Settings;
using WaNut.Core.Tests.TestSupport;

namespace WaNut.Core.Tests.Settings;

public sealed class AppSettingsStoreTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Load_NoFile_AdvancedModeOff()
    {
        Assert.False(new AppSettingsStore(new LauncherPaths(_dir.Path)).Load().AdvancedMode);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var store = new AppSettingsStore(new LauncherPaths(_dir.Path));

        store.Save(new AppSettings { AdvancedMode = true });

        Assert.True(store.Load().AdvancedMode);
        Assert.Contains("\"advancedMode\": true", File.ReadAllText(Path.Combine(_dir.Path, "config.json")), StringComparison.Ordinal);
    }

    [Fact]
    public void SaveThenLoad_FirewallAsked_RoundTrips()
    {
        var store = new AppSettingsStore(new LauncherPaths(_dir.Path));

        store.Save(new AppSettings { FirewallAsked = [@"C:\WaNut\runtimes\java-21\bin\java.exe"] });

        Assert.Equal([@"C:\WaNut\runtimes\java-21\bin\java.exe"], store.Load().FirewallAsked);
    }

    [Fact]
    public void Load_OlderFileWithoutFirewallAsked_GivesEmptyList()
    {
        File.WriteAllText(Path.Combine(_dir.Path, "config.json"), "{\"advancedMode\": true, \"firewallAsked\": null}");

        Assert.Empty(new AppSettingsStore(new LauncherPaths(_dir.Path)).Load().FirewallAsked);
    }

    [Fact]
    public void Load_DamagedFile_GivesDefaults()
    {
        File.WriteAllText(Path.Combine(_dir.Path, "config.json"), "{ not json");

        Assert.False(new AppSettingsStore(new LauncherPaths(_dir.Path)).Load().AdvancedMode);
    }

    [Fact]
    public void Load_NoFile_BackupDefaultsAreFiveAndThirtyDays()
    {
        var rules = new AppSettingsStore(new LauncherPaths(_dir.Path)).Load().Backups;

        Assert.Equal(5, rules.KeepCount);
        Assert.Equal(30, rules.MaxAgeDays);
    }

    [Fact]
    public void SaveThenLoad_BackupRulesRoundTrip()
    {
        var store = new AppSettingsStore(new LauncherPaths(_dir.Path));

        store.Save(new AppSettings { Backups = new BackupRules { KeepCount = 10, MaxAgeDays = 7 } });

        Assert.Equal(new BackupRules { KeepCount = 10, MaxAgeDays = 7 }, store.Load().Backups);
    }

    [Theory]
    [InlineData("{ \"backups\": null }", 5, 30)]
    [InlineData("{ \"backups\": { \"keepCount\": 0, \"maxAgeDays\": -3 } }", 1, 1)]
    [InlineData("{ \"advancedMode\": true }", 5, 30)]
    public void Load_HandEditedBackupRules_AreMadeValid(string json, int keep, int days)
    {
        File.WriteAllText(Path.Combine(_dir.Path, "config.json"), json);

        var rules = new AppSettingsStore(new LauncherPaths(_dir.Path)).Load().Backups;

        Assert.Equal(keep, rules.KeepCount);
        Assert.Equal(days, rules.MaxAgeDays);
    }
}
