namespace WaNut.Core.Backups;

/// <summary>One backup zip in a server's backups/ folder.</summary>
public sealed record BackupInfo(string FilePath, DateTimeOffset CreatedAt, BackupReason Reason, long SizeBytes)
{
    public string FileName => Path.GetFileName(FilePath);
}
