using WaNut.Core.Settings;
using WaNut.Core.Tests.TestSupport;

namespace WaNut.Core.Tests.Settings;

public sealed class ServerSettingsStoreTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private string PropertiesPath => Path.Combine(_dir.Path, GameSettingsStore.PropertiesFileName);

    [Fact]
    public void Load_NoFile_GivesDefaults()
    {
        Assert.Equal(new ServerSettings(), ServerSettingsStore.Load(_dir.Path));
    }

    [Theory]
    [InlineData("server-port=25570\nonline-mode=false\n", 25570, false)]
    [InlineData("server-port=0\nonline-mode=maybe\n", 25565, true)]
    [InlineData("server-port=99999\n", 25565, true)]
    public void Load_ReadsValidValues(string text, int port, bool onlineMode)
    {
        File.WriteAllText(PropertiesPath, text);

        Assert.Equal(new ServerSettings { Port = port, OnlineMode = onlineMode }, ServerSettingsStore.Load(_dir.Path));
    }

    [Fact]
    public void Save_WritesOnlyChangedKeys()
    {
        File.WriteAllText(PropertiesPath, "online-mode=true\r\nserver-port=bad\r\n");
        var loaded = ServerSettingsStore.Load(_dir.Path);

        ServerSettingsStore.Save(_dir.Path, loaded, loaded with { OnlineMode = false });

        Assert.Equal("online-mode=false\r\nserver-port=bad\r\n", File.ReadAllText(PropertiesPath));
    }

    [Fact]
    public void Save_InvalidPort_Throws()
    {
        var loaded = new ServerSettings();

        Assert.Throws<ArgumentOutOfRangeException>(() => ServerSettingsStore.Save(_dir.Path, loaded, loaded with { Port = 70000 }));
    }
}
