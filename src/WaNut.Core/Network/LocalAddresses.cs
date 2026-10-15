using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace WaNut.Core.Network;

/// <summary>Lists this PC's IPv4 addresses that friends can use to join, one per adapter.</summary>
public static class LocalAddresses
{
    // Virtual adapters made by other programs (WSL, virtual machines, Bluetooth). Friends can't reach them, and
    // listing them only makes it harder to pick the right address.
    private static readonly string[] HiddenAdapters =
        ["Hyper-V Virtual", "VirtualBox", "VMware", "Bluetooth", "Npcap Loopback", "Microsoft Wi-Fi Direct"];

    public static IReadOnlyList<LocalAddress> Get() =>
        Select(NetworkInterface.GetAllNetworkInterfaces().Select(adapter => new AdapterInfo(
            adapter.Name,
            adapter.Description,
            adapter.NetworkInterfaceType,
            adapter.OperationalStatus,
            [.. adapter.GetIPProperties().UnicastAddresses.Select(a => a.Address)])));

    /// <summary>Home networks first, then the virtual-LAN tools, each sorted by name.</summary>
    public static IReadOnlyList<LocalAddress> Select(IEnumerable<AdapterInfo> adapters) =>
        [.. adapters
            .Where(a => a.Status == OperationalStatus.Up)
            .Select(a => (Adapter: a, Kind: Classify(a.Name, a.Description)))
            .Where(x => x.Kind != AdapterKind.Lan || IsHomeNetwork(x.Adapter))
            .SelectMany(x => x.Adapter.Addresses.Where(IsReachableIPv4).Select(ip => new LocalAddress(x.Adapter.Name, x.Kind, ip)))
            .OrderBy(a => a.Kind)
            .ThenBy(a => a.AdapterName, StringComparer.CurrentCultureIgnoreCase)];

    /// <summary>Tells the virtual-LAN tools apart by the adapter their installers create.</summary>
    public static AdapterKind Classify(string name, string description)
    {
        var text = name + " " + description;
        if (text.Contains("Radmin", StringComparison.OrdinalIgnoreCase))
        {
            return AdapterKind.RadminVpn;
        }

        if (text.Contains("ZeroTier", StringComparison.OrdinalIgnoreCase))
        {
            return AdapterKind.ZeroTier;
        }

        if (text.Contains("Tailscale", StringComparison.OrdinalIgnoreCase))
        {
            return AdapterKind.Tailscale;
        }

        return text.Contains("Hamachi", StringComparison.OrdinalIgnoreCase) ? AdapterKind.Hamachi : AdapterKind.Lan;
    }

    /// <summary>The form players type into Minecraft's "Server Address" box.</summary>
    public static string Endpoint(IPAddress address, int port) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{address}:{port}");

    private static bool IsHomeNetwork(AdapterInfo adapter) =>
        adapter.Type is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
        && !HiddenAdapters.Any(hidden => adapter.Description.Contains(hidden, StringComparison.OrdinalIgnoreCase)
            || adapter.Name.Contains(hidden, StringComparison.OrdinalIgnoreCase));

    private static bool IsReachableIPv4(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address))
        {
            return false;
        }

        // 169.254.x.x is what Windows picks when no router answered, so nobody else can use it.
        var bytes = address.GetAddressBytes();
        return !(bytes[0] == 169 && bytes[1] == 254);
    }
}
