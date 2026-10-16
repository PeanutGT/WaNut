namespace WaNut.Core.Backups;

/// <summary>Why a backup was made. Part of the file name, so it survives without any index file.</summary>
public enum BackupReason
{
    /// <summary>Made automatically before the server started.</summary>
    Start,

    /// <summary>The user pressed "Back up now".</summary>
    Manual,

    /// <summary>The world as it was just before another backup was restored over it.</summary>
    Restore,

    /// <summary>The world as it was just before the server's type or Minecraft version was changed.</summary>
    Change,

    /// <summary>The world as it was just before another world was imported over it.</summary>
    Import,

    /// <summary>The world as it was just before it was deleted for a fresh one.</summary>
    Reset,
}
