namespace WaNut.Core.Hosting;

public enum ServerStartProblem
{
    ServerJarMissing,
    EulaNotAccepted,
    AlreadyRunning,
}

/// <summary>A server cannot start for a reason the user can fix. The UI turns <see cref="Problem"/> into a message.</summary>
public sealed class ServerStartException(ServerStartProblem problem)
    : Exception($"The server cannot start: {problem}.")
{
    public ServerStartProblem Problem { get; } = problem;
}
