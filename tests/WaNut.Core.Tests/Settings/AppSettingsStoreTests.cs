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
    public void Load_DamagedFile_GivesDefaults()
    {
        File.WriteAllText(Path.Combine(_dir.Path, "config.json"), "{ not json");

        Assert.False(new AppSettingsStore(new LauncherPaths(_dir.Path)).Load().AdvancedMode);
    }
}
