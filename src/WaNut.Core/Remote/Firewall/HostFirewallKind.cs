namespace WaNut.Core.Remote.Firewall;

/// <summary>The firewall running on the VPS itself, not the cloud provider's one in front of it.</summary>
public enum HostFirewallKind
{
    /// <summary>No password-less sudo, so the rules couldn't be read.</summary>
    Unknown,

    /// <summary>Nothing blocks incoming connections.</summary>
    None,

    /// <summary>Plain iptables rules, such as Oracle Cloud's Ubuntu images (saved in /etc/iptables/rules.v4).</summary>
    Iptables,

    Ufw,

    Firewalld,
}
