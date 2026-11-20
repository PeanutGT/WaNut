using System.Net;
using WaNut.Core.Network;

namespace WaNut.Core.Packs;

/// <summary>Picks the one address written into server.properties for the hosted pack.</summary>
public static class ResourcePackAddress
{
    /// <summary>
    /// A virtual-LAN address (Radmin, ZeroTier, Tailscale, Hamachi) when there is one, since that is how friends join
    /// for now and it needs no router setup; otherwise the first home-network address. Null when the PC has none.
    /// </summary>
    public static IPAddress? Pick(IReadOnlyList<LocalAddress> addresses) =>
        (addresses.FirstOrDefault(a => a.Kind is AdapterKind.RadminVpn or AdapterKind.ZeroTier or AdapterKind.Tailscale or AdapterKind.Hamachi)
         ?? addresses.FirstOrDefault(a => a.Kind == AdapterKind.Lan))?.Address;
}
