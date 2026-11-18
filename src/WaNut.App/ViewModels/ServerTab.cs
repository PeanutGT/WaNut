namespace WaNut.App.ViewModels;

/// <summary>The server's tabs, in the order the main window shows them.</summary>
public enum ServerTab
{
    Console,
    Players,
    Game,
    Server,
    Backups,
    Mods,
    Packs,
    Connect,
    Performance,
    Discord,

    /// <summary>Only for a server set to run on a VPS; last so the other tabs keep their numbers.</summary>
    Files,
}
