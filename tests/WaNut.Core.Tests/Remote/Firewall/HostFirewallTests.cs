using WaNut.Core.Remote;
using WaNut.Core.Remote.Firewall;

namespace WaNut.Core.Tests.Remote.Firewall;

public sealed class HostFirewallTests
{
    // Read from a real Oracle Cloud Ubuntu instance on 2026-11-14.
    private const string OracleRules =
        "ipt=-P INPUT ACCEPT\n" +
        "ipt=-A INPUT -m state --state RELATED,ESTABLISHED -j ACCEPT\n" +
        "ipt=-A INPUT -p icmp -j ACCEPT\n" +
        "ipt=-A INPUT -i lo -j ACCEPT\n" +
        "ipt=-A INPUT -p tcp -m state --state NEW -m tcp --dport 22 -j ACCEPT\n" +
        "ipt=-A INPUT -j REJECT --reject-with icmp-host-prohibited\n";

    private const string OracleOutput = "sudo=yes\nsaves=yes\npersistent=yes\n" + OracleRules;

    [Fact]
    public void Parse_OracleDefault_IsIptablesWithOnlySshOpen()
    {
        var firewall = HostFirewall.Parse(OracleOutput, VpsProvider.Oracle);

        Assert.Equal(HostFirewallKind.Iptables, firewall.Kind);
        Assert.True(firewall.IsOpen(22));
        Assert.False(firewall.IsOpen(25565));
    }

    [Fact]
    public void OpenCommand_Oracle_InsertsBeforeTheRejectOnceAndSaves()
    {
        var command = HostFirewall.Parse(OracleOutput, VpsProvider.Oracle).OpenCommand(25565);

        Assert.Equal(
            "(sudo iptables -C INPUT -p tcp -m state --state NEW -m tcp --dport 25565 -j ACCEPT 2>/dev/null || " +
            "sudo iptables -I INPUT 1 -p tcp -m state --state NEW -m tcp --dport 25565 -j ACCEPT) && sudo netfilter-persistent save",
            command);
    }

    [Fact]
    public void OpenCommand_NoNetfilterPersistent_WritesRulesV4()
    {
        var command = HostFirewall.Parse("sudo=yes\nsaves=yes\n" + OracleRules, VpsProvider.Oracle).OpenCommand(25565);

        Assert.EndsWith("&& sudo sh -c 'iptables-save > /etc/iptables/rules.v4'", command);
    }

    [Fact]
    public void IsOpen_PortAddedBeforeReject_IsOpenAndNeedsNothing()
    {
        var output = OracleOutput.Replace(
            "ipt=-A INPUT -m state",
            "ipt=-A INPUT -p tcp -m state --state NEW -m tcp --dport 25565 -j ACCEPT\nipt=-A INPUT -m state",
            StringComparison.Ordinal);
        var firewall = HostFirewall.Parse(output, VpsProvider.Oracle);

        Assert.True(firewall.IsOpen(25565));
        Assert.Null(firewall.OpenCommand(25565));
    }

    [Fact]
    public void IsOpen_PortAddedAfterReject_StillClosed()
    {
        var firewall = HostFirewall.Parse(OracleOutput + "ipt=-A INPUT -p tcp -m tcp --dport 25565 -j ACCEPT\n", VpsProvider.Oracle);

        Assert.False(firewall.IsOpen(25565));
    }

    [Theory]
    [InlineData("ipt=-A INPUT -p tcp -m multiport --dports 22,25560:25570 -j ACCEPT\n", true)]
    [InlineData("ipt=-A INPUT -p udp -m udp --dport 25565 -j ACCEPT\n", false)]
    [InlineData("ipt=-A INPUT -s 10.0.0.0/8 -p tcp -m tcp --dport 25565 -j ACCEPT\n", false)]
    [InlineData("ipt=-A INPUT -j ACCEPT\n", true)]
    public void IsOpen_RuleShapes(string rule, bool open)
    {
        var output = "sudo=yes\nipt=-P INPUT ACCEPT\n" + rule + "ipt=-A INPUT -j DROP\n";

        Assert.Equal(open, HostFirewall.Parse(output, VpsProvider.Other).IsOpen(25565));
    }

    [Fact]
    public void Parse_NothingBlocks_NoneAndNoCommand()
    {
        var firewall = HostFirewall.Parse("sudo=yes\nipt=-P INPUT ACCEPT\n", VpsProvider.Other);

        Assert.Equal(HostFirewallKind.None, firewall.Kind);
        Assert.True(firewall.IsOpen(25565));
        Assert.Null(firewall.OpenCommand(25565));
    }

    [Fact]
    public void Parse_PolicyDrop_IsIptables()
    {
        var firewall = HostFirewall.Parse("sudo=yes\nipt=-P INPUT DROP\nipt=-A INPUT -p tcp -m tcp --dport 22 -j ACCEPT\n", VpsProvider.Other);

        Assert.Equal(HostFirewallKind.Iptables, firewall.Kind);
        Assert.False(firewall.IsOpen(25565));
    }

    [Fact]
    public void Parse_NoSudo_UnknownButOracleStillGetsCommandsToPaste()
    {
        var oracle = HostFirewall.Parse("saves=yes\n", VpsProvider.Oracle);
        var other = HostFirewall.Parse("", VpsProvider.Other);

        Assert.Equal(HostFirewallKind.Unknown, oracle.Kind);
        Assert.Null(oracle.IsOpen(25565));
        Assert.Null(oracle.OpenCommand(25565));
        Assert.Contains("--dport 25565 -j ACCEPT", oracle.ManualCommands(25565));
        Assert.Null(other.ManualCommands(25565));
    }

    [Fact]
    public void Parse_UfwActive_UsesUfwExceptOnOracle()
    {
        const string output = "sudo=yes\nufw=Status: active\nufw=\nufw=To Action From\nufw=22/tcp ALLOW Anywhere\n" + OracleRules;

        var other = HostFirewall.Parse(output, VpsProvider.Other);
        Assert.Equal(HostFirewallKind.Ufw, other.Kind);
        Assert.Equal("sudo ufw allow 25565/tcp", other.OpenCommand(25565));
        Assert.True(other.IsOpen(22));

        Assert.Equal(HostFirewallKind.Iptables, HostFirewall.Parse(output, VpsProvider.Oracle).Kind);
    }

    [Fact]
    public void Parse_UfwInactive_FallsBackToIptables() =>
        Assert.Equal(HostFirewallKind.None, HostFirewall.Parse("sudo=yes\nufw=Status: inactive\nipt=-P INPUT ACCEPT\n", VpsProvider.Other).Kind);

    [Fact]
    public void Parse_Firewalld_ReadsItsPorts()
    {
        var firewall = HostFirewall.Parse("sudo=yes\nfirewalld=running\nfwports=25565/tcp 8080/udp\n", VpsProvider.Other);

        Assert.Equal(HostFirewallKind.Firewalld, firewall.Kind);
        Assert.True(firewall.IsOpen(25565));
        Assert.Equal("sudo firewall-cmd --permanent --add-port=25566/tcp && sudo firewall-cmd --reload", firewall.OpenCommand(25566));
    }

    [Fact]
    public void Command_RunsTheScriptThroughSh() =>
        Assert.StartsWith("sh -c '", HostFirewall.Command);

    [Theory]
    [InlineData("OracleCloud.com", "QEMU", "Standard PC", VpsProvider.Oracle)]
    [InlineData("", "Amazon EC2", "t3.micro", VpsProvider.Aws)]
    [InlineData("7783-7084-3265-9085-8269-3286-77", "Microsoft Corporation", "Virtual Machine", VpsProvider.Azure)]
    [InlineData("", "Google", "Google Compute Engine", VpsProvider.Gcp)]
    [InlineData("", "Hetzner", "vServer", VpsProvider.Other)]
    public void ProviderOf_DmiFields(string asset, string vendor, string product, VpsProvider expected) =>
        Assert.Equal(expected, HostProbe.ProviderOf(asset, vendor, product));

    [Fact]
    public void HostProbe_ReadsDmiFields() =>
        Assert.Equal(VpsProvider.Oracle, HostProbe.Parse("dmi_chassis_asset_tag=OracleCloud.com\ndmi_sys_vendor=QEMU\n").Provider);
}
