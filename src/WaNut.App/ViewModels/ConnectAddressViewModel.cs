using WaNut.App.Resources;
using WaNut.Core.Network;

namespace WaNut.App.ViewModels;

/// <summary>One address on the Connect tab, in the "IP:port" form players paste into Minecraft.</summary>
public sealed class ConnectAddressViewModel(LocalAddress address, int port)
{
    public LocalAddress Address { get; } = address;

    public string Endpoint { get; } = LocalAddresses.Endpoint(address.Address, port);

    /// <summary>The tool's name for a virtual LAN, the adapter's own name ("Wi-Fi", "Ethernet") for the home network.</summary>
    public string Title => Address.Kind == AdapterKind.Lan ? Address.AdapterName : Text.Get($"Connect_Kind_{Address.Kind}");

    public string Hint => Text.Get($"Connect_Hint_{Address.Kind}");

    public string AccessibleName => $"{Title}: {Endpoint}";

    public string CopyLabel => string.Format(Text.Get("Connect_CopyLabel"), Title, Endpoint);
}
