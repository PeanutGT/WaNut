namespace WaNut.Core.Workspace;

public sealed class TextEditException(TextEditProblem problem) : Exception($"Can't edit this file as text: {problem}.")
{
    public TextEditProblem Problem { get; } = problem;
}
