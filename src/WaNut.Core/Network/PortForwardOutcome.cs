namespace WaNut.Core.Network;

/// <summary>What happened when WaNut asked the router to forward the server's port (UPnP or NAT-PMP).</summary>
public enum PortForwardOutcome
{
    /// <summary>The router forwards the port; friends can join on the public address.</summary>
    Opened,

    /// <summary>No router answered: UPnP is off on the router, or the router has none.</summary>
    NoRouter,

    /// <summary>The router answered but would not add the rule.</summary>
    Refused,

    /// <summary>Another device on the network already uses this port on the router.</summary>
    Conflict,

    /// <summary>The router's own internet address is a shared carrier-grade NAT one (100.64.0.0/10): the ISP blocks incoming connections.</summary>
    Cgnat,

    /// <summary>The router's internet address is a private one: a second router (often the ISP's modem) sits in front of it.</summary>
    DoubleNat,
}
