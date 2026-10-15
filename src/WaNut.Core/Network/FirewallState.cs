namespace WaNut.Core.Network;

/// <summary>Whether Windows Defender Firewall lets friends reach a program.</summary>
public enum FirewallState
{
    /// <summary>An inbound allow rule covers both private and public networks (Radmin VPN counts as public).</summary>
    Allowed,

    /// <summary>No rule, or one that covers only some networks: Windows may silently drop friends.</summary>
    NotAllowed,

    /// <summary>An inbound block rule exists, usually because Windows' own prompt was cancelled once.</summary>
    Blocked,

    /// <summary>The rules couldn't be read (not Windows, or PowerShell failed).</summary>
    Unknown,
}
