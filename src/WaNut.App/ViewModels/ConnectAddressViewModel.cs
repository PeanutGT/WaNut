using WaNut.App.Resources;
using WaNut.Core.Network;

namespace WaNut.App.ViewModels;

/// <summary>One address on the Connect tab, in the "IP:port" form players paste into Minecraft.</summary>
public sealed class ConnectAddressViewModel
{
    public ConnectAddressViewModel(LocalAddress address, int port)
    {
        Kind = address.Kind;
        AdapterName = address.AdapterName;
        Endpoint = LocalAddresses.Endpoint(address.Address, port);
    }

    /// <summary>An address that is a name rather than an IP, such as the playit.gg tunnel's "abc.gl.joinmc.link".</summary>
    public ConnectAddressViewModel(AdapterKind kind, string endpoint)
    {
        Kind = kind;
        AdapterName = string.Empty;
        Endpoint = endpoint;
    }

    public AdapterKind Kind { get; }

    public string AdapterName { get; }

    public string Endpoint { get; }

    /// <summary>The tool's name for a virtual LAN, the adapter's own name ("Wi-Fi", "Ethernet") for the home network.</summary>
    public string Title => Kind == AdapterKind.Lan ? AdapterName : Text.Get($"Connect_Kind_{Kind}");

    public string Hint => Text.Get($"Connect_Hint_{Kind}");

    public string AccessibleName => $"{Title}: {Endpoint}";

    public string CopyLabel => string.Format(Text.Get("Connect_CopyLabel"), Title, Endpoint);
}
