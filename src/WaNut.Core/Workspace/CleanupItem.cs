using WaNut.Core.Storage;

namespace WaNut.Core.Workspace;

/// <summary>Why the clean-up suggests an entry.</summary>
public enum CleanupReason
{
    /// <summary>A log or crash report older than <see cref="CleanupRules.LogRetentionDays"/>.</summary>
    OldLog,

    /// <summary>*.jar.disabled, *.jar.old or a file in mods-disabled/.</summary>
    DisabledMod,

    /// <summary>A WaNut backup beyond the backup rules (keep count, max age).</summary>
    OldBackup,

    /// <summary>A loose copy of a world (world_backup, old_world) that gets packed into backups/.</summary>
    LooseWorldCopy,
}

/// <summary>What happens to an entry: deleted, or packed into a .tar.gz in backups/ and then removed.</summary>
public enum CleanupAction
{
    Delete,
    Archive,
}

/// <summary>
/// One suggestion of a <see cref="CleanupPlan"/>. <see cref="SizeBytes"/> includes everything inside a folder.
/// <see cref="Risky"/> marks world data (region files): such an entry is never checked by default.
/// </summary>
public sealed record CleanupItem(
    ServerFileNode Node,
    CleanupReason Reason,
    CleanupAction Action,
    long SizeBytes,
    bool Risky,
    string? ArchivePath = null)
{
    public string Path => Node.Path;

    /// <summary>
    /// Old logs and old backups are checked; disabled mods may be wanted back, so they wait for the person to tick
    /// them, and anything holding world data does too.
    /// </summary>
    public bool CheckedByDefault => !Risky && Reason is CleanupReason.OldLog or CleanupReason.OldBackup;
}
