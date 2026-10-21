namespace WaNut.Core.Hosting;

/// <summary>
/// Owns the running servers. Several may run at once (blueprint decision 5), but each server only once.
/// </summary>
public sealed class ServerHost
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, ServerProcess> _processes = [];

    /// <summary>The ids of the servers that are running.</summary>
    public IReadOnlyList<string> RunningInstanceIds
    {
        get
        {
            lock (_gate)
            {
                return [.. _processes.Where(p => p.Value.IsRunning).Select(p => p.Key)];
            }
        }
    }

    /// <summary>The server's latest process, running or exited, or null when it never started.</summary>
    public ServerProcess? Get(string instanceId)
    {
        lock (_gate)
        {
            return _processes.GetValueOrDefault(instanceId);
        }
    }

    /// <param name="beforeStart">Runs before the process starts, so subscribers do not miss its first lines.</param>
    public ServerProcess Start(string instanceId, ServerLaunch launch, Action<ServerProcess>? beforeStart = null)
    {
        lock (_gate)
        {
            var previous = _processes.GetValueOrDefault(instanceId);
            if (previous is { IsRunning: true })
            {
                throw new ServerStartException(ServerStartProblem.AlreadyRunning);
            }

            var process = new ServerProcess(launch);
            beforeStart?.Invoke(process);
            process.Start();

            previous?.Dispose();
            _processes[instanceId] = process;
            return process;
        }
    }
}
