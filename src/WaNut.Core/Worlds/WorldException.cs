namespace WaNut.Core.Worlds;

public sealed class WorldException(WorldProblem problem, string? detail = null)
    : Exception(detail is null ? problem.ToString() : $"{problem}: {detail}")
{
    public WorldProblem Problem { get; } = problem;
}
