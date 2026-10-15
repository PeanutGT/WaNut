namespace WaNut.Core.Backups;

public enum BackupProblem
{
    /// <summary>level-name in server.properties is not a plain folder name, e.g. "../other".</summary>
    UnsafeWorldName,

    /// <summary>The zip cannot be read, is empty, or has paths that would land outside the server folder.</summary>
    DamagedBackup,

    /// <summary>The backup file is gone.</summary>
    BackupMissing,
}
