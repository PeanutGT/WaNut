namespace WaNut.Core.Remote.Files;

/// <summary>A folder on the VPS that WaNut must not manage or delete from. Nothing on the VPS was changed.</summary>
public sealed class WorkspaceRootException(string root, WorkspaceRootProblem problem)
    : Exception($"WaNut won't manage '{root}' on the VPS ({problem}). Nothing was changed.")
{
    public string Root { get; } = root;

    public WorkspaceRootProblem Problem { get; } = problem;
}
