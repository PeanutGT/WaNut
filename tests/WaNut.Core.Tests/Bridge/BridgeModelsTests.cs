using System.Text.Json;
using WaNut.Core.Bridge;

namespace WaNut.Core.Tests.Bridge;

public sealed class BridgeModelsTests
{
    [Fact]
    public void Json_RoundTrip_KeepsEveryField()
    {
        var config = new BridgeConfig
        {
            ServerId = "abc",
            LogFile = "/srv/mc/logs/latest.log",
            RconPort = 30000,
            RconPassword = "pw",
            BotToken = "token",
            ChannelId = "123456789012345678",
            WebhookUrl = "https://discord.com/api/webhooks/1/x",
            Presentation = DiscordPresentation.Embed,
            AdminRoleId = "987654321098765432",
            Filters = new BridgeFilters { Chat = false, RemoteConsole = true, IgnoredPrefixes = ["!", "#"] },
        };

        var back = BridgeConfigJson.Deserialize(BridgeConfigJson.Serialize(config));

        Assert.Equal(config with { Filters = config.Filters with { IgnoredPrefixes = [] } }, back with { Filters = back.Filters with { IgnoredPrefixes = [] } });
        Assert.Equal(["!", "#"], back.Filters.IgnoredPrefixes);
    }

    [Fact]
    public void Json_Serialize_UsesCamelCaseNamesAndStringEnums()
    {
        var json = BridgeConfigJson.Serialize(new BridgeConfig { Presentation = DiscordPresentation.PlainText });

        Assert.Contains("\"presentation\": \"plainText\"", json, StringComparison.Ordinal);
        Assert.Contains("\"schemaVersion\": 1", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Json_SnowflakeIds_StayExactStrings()
    {
        var json = BridgeConfigJson.Serialize(new BridgeConfig { ChannelId = "1234567890123456789" });

        Assert.Equal("1234567890123456789", BridgeConfigJson.Deserialize(json).ChannelId);
    }

    [Fact]
    public void Json_MissingFields_GetDefaults()
    {
        var config = BridgeConfigJson.Deserialize("{}");

        Assert.Equal(25575, config.RconPort);
        Assert.True(config.Filters.Chat);
        Assert.False(config.Filters.RemoteConsole);
        Assert.Equal(DiscordPresentation.Webhook, config.Presentation);
    }

    [Fact]
    public void Json_NullDocument_Throws()
    {
        Assert.Throws<JsonException>(() => BridgeConfigJson.Deserialize("null"));
    }

    [Fact]
    public void Defaults_MatchTheDocumentedOptionTable()
    {
        var filters = new BridgeFilters();

        Assert.True(filters.Chat && filters.JoinLeave && filters.DeathsAndAdvancements && filters.ServerState);
        Assert.True(filters.OnlineCountStatus && filters.ListCommand);
        Assert.False(filters.RemoteConsole);
        Assert.Empty(filters.IgnoredPrefixes);
    }

    [Fact]
    public void Target_Local_IsValid()
    {
        Assert.True(DeploymentTarget.Local.IsValid);
        Assert.True(new DeploymentTarget(DeploymentKind.Local, null, BridgeRunMode.ScheduledTask).IsValid);
    }

    [Fact]
    public void Target_Vps_IsValidOnlyWithVpsId()
    {
        Assert.True(DeploymentTarget.OnVps("v1").IsValid);
        Assert.True(DeploymentTarget.OnVps("v1", BridgeRunMode.Docker).IsValid);
        Assert.False(new DeploymentTarget(DeploymentKind.Vps).IsValid);
        Assert.False(DeploymentTarget.OnVps(" ").IsValid);
    }

    [Theory]
    [InlineData(BridgeRunMode.Systemd)]
    [InlineData(BridgeRunMode.Nohup)]
    [InlineData(BridgeRunMode.Docker)]
    public void Target_LocalWithVpsOnlyMode_IsInvalid(BridgeRunMode mode)
    {
        Assert.False(new DeploymentTarget(DeploymentKind.Local, null, mode).IsValid);
    }

    [Theory]
    [InlineData(BridgeRunMode.DetachedProcess)]
    [InlineData(BridgeRunMode.ScheduledTask)]
    [InlineData(BridgeRunMode.WindowsService)]
    public void Target_VpsWithWindowsOnlyMode_IsInvalid(BridgeRunMode mode)
    {
        Assert.False(DeploymentTarget.OnVps("v1", mode).IsValid);
    }

    [Fact]
    public void Target_LocalWithVpsId_IsInvalid()
    {
        Assert.False(new DeploymentTarget(DeploymentKind.Local, "v1").IsValid);
    }

    [Fact]
    public void Status_NotInstalled_HasNoConnections()
    {
        var status = BridgeStatus.NotInstalled;

        Assert.Equal(BridgeState.NotInstalled, status.State);
        Assert.False(status.RconConnected || status.DiscordConnected);
    }
}
