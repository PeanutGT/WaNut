using WaNut.Core.Bridge;
using WaNut.Core.Instances;
using WaNut.Core.Tests.TestSupport;

namespace WaNut.Core.Tests.Bridge;

public sealed class BridgeSettingsStoreTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Save_ThenLoad_GivesTheFormBack()
    {
        var store = new BridgeSettingsStore(new LauncherPaths(_temp.Path), new ReversingProtector());
        var settings = new BridgeSettings
        {
            StartOnBoot = true,
            Config = new BridgeConfig
            {
                ServerId = "srv",
                BotToken = "token",
                WebhookUrl = "https://discord.com/api/webhooks/1/x",
                ChannelId = "123",
                Presentation = DiscordPresentation.Embed,
                Language = "zh-TW",
                AdminRoleId = "9",
                Filters = new BridgeFilters { RemoteConsole = true, IgnoredPrefixes = ["!", "#"] },
            },
        };

        store.Save("srv", settings);
        var back = store.Load("srv");

        Assert.NotNull(back);
        Assert.True(back.StartOnBoot);
        Assert.Equal("token", back.Config.BotToken);
        Assert.Equal(settings.Config.WebhookUrl, back.Config.WebhookUrl);
        Assert.Equal(DiscordPresentation.Embed, back.Config.Presentation);
        Assert.Equal("zh-TW", back.Config.Language);
        Assert.True(back.Config.Filters.RemoteConsole);
        Assert.Equal(["!", "#"], back.Config.Filters.IgnoredPrefixes);
    }

    [Fact]
    public void Save_Secrets_AreNotWrittenAsPlainText_AndRconFieldsAreDropped()
    {
        var store = new BridgeSettingsStore(new LauncherPaths(_temp.Path), new ReversingProtector());

        store.Save("srv", new BridgeSettings
        {
            Config = new BridgeConfig { BotToken = "secret-token", WebhookUrl = "https://discord.com/api/webhooks/1/hookvalue", RconPassword = "rcon-pw", LogFile = "C:\\x\\latest.log" },
        });

        var text = File.ReadAllText(Path.Combine(store.Folder, "srv.json"));
        Assert.DoesNotContain("secret-token", text, StringComparison.Ordinal);
        Assert.DoesNotContain("hookvalue", text, StringComparison.Ordinal);
        Assert.DoesNotContain("rcon-pw", text, StringComparison.Ordinal);
        Assert.DoesNotContain("latest.log", text, StringComparison.Ordinal);
        Assert.Contains(BridgeSecrets.Prefix, text, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_NothingSavedOrDamaged_ReturnsNull()
    {
        var store = new BridgeSettingsStore(new LauncherPaths(_temp.Path), new ReversingProtector());
        Assert.Null(store.Load("srv"));

        Directory.CreateDirectory(store.Folder);
        File.WriteAllText(Path.Combine(store.Folder, "srv.json"), "{ not json");

        Assert.Null(store.Load("srv"));
    }

    [Fact]
    public void Delete_RemovesTheFile_AndIgnoresMissingOnes()
    {
        var store = new BridgeSettingsStore(new LauncherPaths(_temp.Path), new ReversingProtector());
        store.Save("srv", new BridgeSettings());

        store.Delete("srv");
        store.Delete("srv");

        Assert.Null(store.Load("srv"));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("")]
    public void Save_IdOutsideTheFolder_Throws(string id)
    {
        var store = new BridgeSettingsStore(new LauncherPaths(_temp.Path), new ReversingProtector());

        Assert.Throws<ArgumentException>(() => store.Save(id, new BridgeSettings()));
    }
}
