using WaNut.Core.Bridge;
using WaNut.Core.Bridge.Vps;

namespace WaNut.Core.Tests.Bridge.Vps;

public sealed class VpsBridgeScriptsTests
{
    [Fact]
    public void Unit_RestartsOnCrashButNotOnABadConfig_AndStartsAtBoot()
    {
        var unit = VpsBridgeScripts.Unit(VpsBridgeScripts.SystemBase, "srv", "ubuntu");

        Assert.Contains("User=ubuntu", unit, StringComparison.Ordinal);
        Assert.Contains("Restart=always", unit, StringComparison.Ordinal);
        Assert.Contains("RestartPreventExitStatus=2", unit, StringComparison.Ordinal);
        Assert.Contains("WantedBy=multi-user.target", unit, StringComparison.Ordinal);
        Assert.Contains("ExecStart=\"/opt/wanut-bridge/WaNut.Bridge\" --config \"/opt/wanut-bridge/srv/bridge.config.json\"", unit, StringComparison.Ordinal);
    }

    [Fact]
    public void UnitName_OddCharacters_BecomeDashes()
    {
        Assert.Equal("wanut-bridge-my-server.service", VpsBridgeScripts.UnitName("my server"));
    }

    [Fact]
    public void WriteConfig_NeverLeavesItReadableByOthers()
    {
        var script = VpsBridgeScripts.WriteConfig("/opt/wanut-bridge", "srv");

        Assert.StartsWith("umask 077;", script, StringComparison.Ordinal);
        Assert.Contains("chmod 600", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Scripts_QuoteAServerIdWithAQuote()
    {
        var script = VpsBridgeScripts.StartPlain("/home/u/wanut/bridge", "it's");

        Assert.Contains("'/home/u/wanut/bridge/it'\\''s'", script, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("none", BridgeState.NotInstalled)]
    [InlineData("unit active", BridgeState.Starting)]
    [InlineData("unit failed", BridgeState.Failed)]
    [InlineData("unit inactive", BridgeState.Stopped)]
    [InlineData("plain running", BridgeState.Starting)]
    [InlineData("plain stopped", BridgeState.Stopped)]
    [InlineData("{\"state\":\"running\",\"processId\":5}", BridgeState.Running)]
    public void ParseStatus_EachAnswer_GivesItsState(string output, BridgeState expected)
    {
        Assert.Equal(expected, SshVpsBridgeDeployer.ParseStatus(output + "\n").State);
    }
}
