namespace WaNut.Core.Hosting;

/// <summary>
/// One run of a server, wherever it runs (this PC today, a remote host later). Created stopped by an
/// <see cref="IServerRuntime"/>, then started once. Events are raised on background threads.
/// </summary>
public interface IRunningServer : IServerConsole, IDisposable
{
    event Action<ServerStatus>? StatusChanged;

    event Action? PlayersChanged;

    ServerStatus Status { get; }

    /// <summary>Completes with the exit code once the server has ended and all its output has been read.</summary>
    Task<int> Exited { get; }

    bool IsRunning { get; }

    /// <summary>True from the moment the server has ended, before its players are cleared and its status changes.</summary>
    bool HasExited { get; }

    /// <summary>True once <see cref="Kill"/> was called, so a non-zero exit code is not mistaken for a crash.</summary>
    bool WasKilled { get; }

    /// <summary>"stop" was sent, by WaNut or typed in the console: an exit after it was asked for, not a crash.</summary>
    bool StopRequested { get; }

    IReadOnlyList<string> Players { get; }

    void Start();

    /// <summary>
    /// Asks the server to save the world and exit, the same as typing "stop". Returns false if it is still running
    /// after <paramref name="timeout"/>; the caller then asks the user before calling <see cref="Kill"/>.
    /// </summary>
    Task<bool> StopAsync(TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>The server's current memory and CPU time, or null when it is not running or the host won't say.</summary>
    ProcessUsage? GetUsage();

    /// <summary>Ends the server without saving. Only for when the user agreed after a stop timed out.</summary>
    void Kill();
}
