namespace WaNut.Core.Bridge.Local;

/// <summary>What the local deployer needs from Windows: background processes and the user's own scheduled tasks.</summary>
public interface ILocalBridgeHost
{
    /// <summary>Starts the executable with no window, apart from WaNut's process tree (A1). Returns its process id.</summary>
    int Start(string executable, IReadOnlyList<string> arguments);

    /// <summary>True while a bridge process with that id runs (an id reused by another program does not count).</summary>
    bool IsRunning(int processId);

    void Kill(int processId);

    /// <summary>Creates or replaces a task that starts the bridge when this user signs in (A2), and starts it now.</summary>
    Task RegisterTaskAsync(string taskName, string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default);

    /// <summary>Ends the task's running bridge, if any.</summary>
    Task EndTaskAsync(string taskName, CancellationToken cancellationToken = default);

    /// <summary>Does nothing when there is no such task.</summary>
    Task DeleteTaskAsync(string taskName, CancellationToken cancellationToken = default);
}
