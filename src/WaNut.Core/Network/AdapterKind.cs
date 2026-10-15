namespace WaNut.Core.Network;

/// <summary>What kind of network an address belongs to, so the user knows which one to give which friends.</summary>
public enum AdapterKind
{
    /// <summary>Wi-Fi or Ethernet: friends on the same router.</summary>
    Lan,
    RadminVpn,
    ZeroTier,
    Tailscale,
    Hamachi,
}
