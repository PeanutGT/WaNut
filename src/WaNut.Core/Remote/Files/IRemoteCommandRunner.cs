namespace WaNut.Core.Remote.Files;

/// <summary>Runs sh commands on the VPS. <see cref="VpsSession"/> is the real one; services take this so tests can stand in for it.</summary>
public interface IRemoteCommandRunner
{
    /// <summary>Runs <paramref name="command"/>, with <paramref name="input"/> as its standard input when given.</summary>
    Task<RemoteCommandResult> RunAsync(string command, string? input = null, CancellationToken cancellationToken = default);
}
