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

    /// <summary>The router's internet address, once WaNut forwarded the port on it: friends anywhere.</summary>
    Internet,

    /// <summary>The server's playit.gg tunnel: friends anywhere, even when the router lets nothing in.</summary>
    Playit,
}
