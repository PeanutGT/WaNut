namespace WaNut.Core.Backups;

public sealed class BackupException(BackupProblem problem, string? detail = null)
    : Exception(detail is null ? problem.ToString() : $"{problem}: {detail}")
{
    public BackupProblem Problem { get; } = problem;
}
