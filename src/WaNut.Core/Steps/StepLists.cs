using WaNut.Core.Instances;

namespace WaNut.Core.Steps;

/// <summary>
/// The steps the loading strip shows for each job. Step names double as the end of their UI text keys
/// (<c>Steps_&lt;step&gt;</c>), so one name means the same thing in every job.
/// </summary>
public static class StepLists
{
    public const string Create = "Create";
    public const string Start = "Start";
    public const string Stop = "Stop";
    public const string Backup = "Backup";

    /// <summary>Making a new server: the downloads, the files, then the world or pack it was made for.</summary>
    /// <param name="installsLoader">Forge and NeoForge run their own installer.</param>
    /// <param name="importsWorld">A world is copied in.</param>
    /// <param name="installsWorldMods">The imported world's mods are installed too.</param>
    /// <param name="installsModpack">The server is made from a modpack.</param>
    public static StepFlow ForCreate(bool installsLoader, bool importsWorld, bool installsWorldMods, bool installsModpack)
    {
        List<string> steps = ["Version", "Java", "ServerFiles"];
        if (installsLoader)
        {
            steps.Add("Loader");
        }

        steps.Add("Settings");
        if (importsWorld)
        {
            steps.Add("CopyWorld");
            if (installsWorldMods)
            {
                steps.Add("WorldMods");
            }
        }
        else if (installsModpack)
        {
            steps.Add("Modpack");
        }

        steps.Add("Ready");
        return new StepFlow(Create, steps);
    }

    /// <summary>The step a stage of server creation belongs to.</summary>
    public static string ForStage(CreateServerStage stage) => stage switch
    {
        CreateServerStage.ReadingVersion => "Version",
        CreateServerStage.DownloadingJava => "Java",
        CreateServerStage.DownloadingServer => "ServerFiles",
        CreateServerStage.InstallingLoader => "Loader",
        CreateServerStage.BackingUp => "Backup",
        _ => "Settings",
    };

    /// <summary>Starting a server: the backup before every start, Java, then the server itself until it is online.</summary>
    /// <param name="onVps">It starts on the user's VPS.</param>
    /// <param name="usesVpsCopy">The folder stayed on the VPS after the last stop: nothing is backed up or uploaded.</param>
    public static StepFlow ForStart(bool onVps, bool usesVpsCopy = false) => new(Start, (onVps, usesVpsCopy) switch
    {
        (true, true) => ["Connect", "Java", "Launch", "LoadWorld", "Online"],
        (true, false) => ["Backup", "Connect", "Java", "Upload", "Launch", "LoadWorld", "Online"],
        _ => ["Backup", "Java", "Launch", "LoadWorld", "Online"],
    });

    /// <summary>Stopping: the server saves players and world and exits; a VPS server's folder may then come back here.</summary>
    /// <param name="bringsFolderBack">A VPS server whose folder comes back after it stops.</param>
    public static StepFlow ForStop(bool bringsFolderBack) => new(Stop, bringsFolderBack
        ? ["Players", "SaveWorld", "Close", "BringBack", "Stopped"]
        : ["Players", "SaveWorld", "Close", "Stopped"]);

    /// <summary>A backup: a running server writes its world to disk first, then the world is zipped.</summary>
    public static StepFlow ForBackup(bool serverRunning) => new(Backup, serverRunning
        ? ["Flush", "Pack", "Saved"]
        : ["Pack", "Saved"]);
}
