namespace WaNut.Core.Onboarding;

/// <summary>
/// The peanut's tour of the main window (decision 17). The server tabs only exist once a server does, so the tour
/// comes in two parts: the window itself, then the tabs, which wait for the first server when there is none yet.
/// </summary>
public static class GuideTour
{
    // Tab positions match the TabControl in MainWindow.xaml.
    public static readonly IReadOnlyList<GuideStop> Intro =
    [
        new("Welcome", GuideTarget.None, "online"),
        new("Create", GuideTarget.CreateServer, "no-servers"),
        new("Servers", GuideTarget.ServerList, "empty-list"),
        new("AppSettings", GuideTarget.AppSettings, "updating"),
    ];

    public static readonly IReadOnlyList<GuideStop> Tabs =
    [
        new("StartStop", GuideTarget.StartStop, "starting"),
        new("Console", GuideTarget.Tab, "online", 0),
        new("Players", GuideTarget.Tab, "no-players", 1),
        new("Game", GuideTarget.Tab, "stopped", 2),
        new("Server", GuideTarget.Tab, "saving", 3),
        new("Backups", GuideTarget.Tab, "backup-done", 4),
        new("Mods", GuideTarget.Tab, "type-modpack", 5),
        new("Connect", GuideTarget.Tab, "friends", 6),
    ];

    /// <summary>Ends the intro when there is no server yet: the tabs part follows the first one.</summary>
    public static readonly GuideStop Later = new("Later", GuideTarget.None, "starting");

    public static readonly GuideStop Done = new("Done", GuideTarget.None, "online");

    /// <summary>Every stop the tour can show, for checking that each has its texts and picture.</summary>
    public static IEnumerable<GuideStop> All => [.. Intro, .. Tabs, Later, Done];

    /// <summary>The stops still to show, or none when the tour has nothing left for now.</summary>
    public static IReadOnlyList<GuideStop> Plan(bool introDone, bool tabsDone, bool hasServer)
    {
        if (!introDone)
        {
            return hasServer && !tabsDone ? [.. Intro, .. Tabs, Done] : [.. Intro, tabsDone ? Done : Later];
        }

        return hasServer && !tabsDone ? [.. Tabs, Done] : [];
    }

    /// <summary>True when the stops point at a server's header or tabs, so a server has to be selected first.</summary>
    public static bool NeedsServer(IEnumerable<GuideStop> stops) =>
        stops.Any(s => s.Target is GuideTarget.StartStop or GuideTarget.Tab);
}
