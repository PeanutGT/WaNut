namespace WaNut.Core.Bridge;

/// <summary>Things a working deployment should still tell the user.</summary>
public enum BridgeDeployWarning
{
    /// <summary>The VPS firewall lets the RCON port in from outside; only the password protects it.</summary>
    RconPortOpen,

    /// <summary>The VPS firewall could not be read (no password-less sudo), so the RCON port may be open.</summary>
    RconPortUnchecked,

    /// <summary>Runs as a plain background process: gone after the PC or VPS restarts until deployed again.</summary>
    NoStartOnBoot,

    /// <summary>The bot can't read message text: turn on Message Content Intent in the Discord developer portal.</summary>
    MessageContentIntentOff,
}
