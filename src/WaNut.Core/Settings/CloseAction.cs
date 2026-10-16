namespace WaNut.Core.Settings;

/// <summary>What closing WaNut's window does while a server runs.</summary>
public enum CloseAction
{
    /// <summary>Ask each time (the default).</summary>
    Ask,

    /// <summary>Hide WaNut in the notification area and keep the server running.</summary>
    Tray,

    /// <summary>Stop the server, saving the world, then close.</summary>
    Stop,
}
