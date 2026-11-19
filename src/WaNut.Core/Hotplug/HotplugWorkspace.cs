namespace WaNut.Core.Hotplug;

/// <summary>
/// The folders on the VPS that a datapack hot-plug works in. Every path is relative to the server folder and uses '/'.
/// </summary>
/// <param name="StagingFolder">Where uploads land (as .part files) and wait to be checked.</param>
/// <param name="BackupFolder">Where replaced or removed datapacks are kept, with a time stamp in the name.</param>
/// <param name="DatapacksFolder">The world's own datapacks folder, on the same file system as both folders above.</param>
/// <param name="UsedWorldFallback">The default folders are on another mount than the world, so these live under the world folder.</param>
public sealed record HotplugWorkspace(string StagingFolder, string BackupFolder, string DatapacksFolder, bool UsedWorldFallback)
{
    public const string StagingName = ".nut-staging";
    public const string BackupName = ".nut-backup";
    public const string DatapacksName = "datapacks";

    /// <summary>The lock file that keeps a second hot-plug out while one runs.</summary>
    public string LockFile => StagingFolder[..StagingFolder.LastIndexOf('/')] + "/.lock";
}
