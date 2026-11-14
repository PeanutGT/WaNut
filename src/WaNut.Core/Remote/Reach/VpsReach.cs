namespace WaNut.Core.Remote.Reach;

/// <summary>What happened when this PC tried the server's address on the VPS, the way a friend's Minecraft would.</summary>
public enum VpsReach
{
    /// <summary>The server answered: friends can join.</summary>
    Open,

    /// <summary>
    /// Turned away right away: the VPS's own firewall rejects the port (Oracle's Ubuntu images do this with
    /// iptables), or nothing listens on it.
    /// </summary>
    Refused,

    /// <summary>No answer at all: usually the cloud provider's firewall (security list or group) drops it.</summary>
    NoAnswer,

    /// <summary>The address couldn't be looked up or this PC has no route to it; says nothing about the VPS.</summary>
    Unknown,
}
