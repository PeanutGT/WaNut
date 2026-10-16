namespace WaNut.Core.Modpacks;

public sealed class ModpackException(ModpackProblem problem, string? detail = null)
    : Exception(detail is null ? problem.ToString() : $"{problem}: {detail}")
{
    public ModpackProblem Problem { get; } = problem;

    /// <summary>For <see cref="ModpackProblem.UnsupportedLoader"/>: the loader's name, such as "forge".</summary>
    public string? Detail { get; } = detail;
}
