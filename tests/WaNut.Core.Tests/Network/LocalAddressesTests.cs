using System.Net;
using System.Net.NetworkInformation;
using WaNut.Core.Network;

namespace WaNut.Core.Tests.Network;

public sealed class LocalAddressesTests
{
    private static AdapterInfo Adapter(
        string name, string description, params string[] addresses) =>
        Adapter(name, description, NetworkInterfaceType.Ethernet, OperationalStatus.Up, addresses);

    private static AdapterInfo Adapter(
        string name, string description, NetworkInterfaceType type, OperationalStatus status, params string[] addresses) =>
        new(name, description, type, status, [.. addresses.Select(IPAddress.Parse)]);

    [Theory]
    [InlineData("Radmin VPN", "Famatech Radmin VPN Ethernet Adapter", AdapterKind.RadminVpn)]
    [InlineData("Ethernet 3", "ZeroTier Virtual Port", AdapterKind.ZeroTier)]
    [InlineData("Tailscale", "Tailscale Tunnel", AdapterKind.Tailscale)]
    [InlineData("Ethernet 2", "LogMeIn Hamachi Virtual Ethernet Adapter", AdapterKind.Hamachi)]
    [InlineData("Wi-Fi", "Intel(R) Wi-Fi 6E AX211 160MHz", AdapterKind.Lan)]
    [InlineData("乙太網路", "Realtek PCIe GbE Family Controller", AdapterKind.Lan)]
    public void Classify_KnownAdapters_ReturnsTheirTool(string name, string description, AdapterKind expected)
    {
        Assert.Equal(expected, LocalAddresses.Classify(name, description));
    }

    [Fact]
    public void Select_KeepsOnlyReachableIPv4()
    {
        var addresses = LocalAddresses.Select(
        [
            Adapter("Wi-Fi", "Intel Wi-Fi", "192.168.1.23", "fe80::1", "169.254.10.2"),
            Adapter("Loopback Pseudo-Interface 1", "Software Loopback Interface 1", NetworkInterfaceType.Loopback, OperationalStatus.Up, "127.0.0.1"),
        ]);

        var address = Assert.Single(addresses);
        Assert.Equal("Wi-Fi", address.AdapterName);
        Assert.Equal(IPAddress.Parse("192.168.1.23"), address.Address);
    }

    [Fact]
    public void Select_HidesDisconnectedAndVirtualMachineAdapters()
    {
        var addresses = LocalAddresses.Select(
        [
            Adapter("Ethernet", "Realtek", NetworkInterfaceType.Ethernet, OperationalStatus.Down, "192.168.1.5"),
            Adapter("vEthernet (WSL)", "Hyper-V Virtual Ethernet Adapter", "172.20.0.1"),
            Adapter("VirtualBox Host-Only Network", "VirtualBox Host-Only Ethernet Adapter", "192.168.56.1"),
            Adapter("Teredo", "Teredo Tunneling Pseudo-Interface", NetworkInterfaceType.Tunnel, OperationalStatus.Up, "10.0.0.9"),
        ]);

        Assert.Empty(addresses);
    }

    [Fact]
    public void Select_HomeNetworkFirstThenVirtualLanTools()
    {
        var addresses = LocalAddresses.Select(
        [
            Adapter("Tailscale", "Tailscale Tunnel", NetworkInterfaceType.Unknown, OperationalStatus.Up, "100.101.102.103"),
            Adapter("Radmin VPN", "Famatech Radmin VPN Ethernet Adapter", "26.12.34.56"),
            Adapter("Wi-Fi", "Intel Wi-Fi", "192.168.1.23"),
        ]);

        Assert.Equal(
            [AdapterKind.Lan, AdapterKind.RadminVpn, AdapterKind.Tailscale],
            addresses.Select(a => a.Kind));
    }

    [Fact]
    public void Endpoint_JoinsAddressAndPort()
    {
        Assert.Equal("26.12.34.56:25565", LocalAddresses.Endpoint(IPAddress.Parse("26.12.34.56"), 25565));
    }
}
