using WaNut.Core.Backups;

namespace WaNut.Core.Workspace;

/// <summary>What the Files tab's clean-up looks for. Backups follow the same rules as the Backups tab.</summary>
public sealed record CleanupRules
{
    public int LogRetentionDays { get; init; } = 7;

    public BackupRules Backups { get; init; } = new();
}
