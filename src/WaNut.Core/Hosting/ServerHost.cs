namespace WaNut.Core.Hosting;

/// <summary>
/// Owns the running server. Only one server runs at a time (blueprint decision 5).
/// </summary>
public sealed class ServerHost
{
    private readonly Lock _gate = new();

    public ServerProcess? Current { get; private set; }

    public string? CurrentInstanceId { get; private set; }

    /// <summary>The id of the server that is running, or null when none is.</summary>
    public string? RunningInstanceId
    {
        get
        {
            lock (_gate)
            {
                return Current is { IsRunning: true } ? CurrentInstanceId : null;
            }
        }
    }

    /// <param name="beforeStart">Runs before the process starts, so subscribers do not miss its first lines.</param>
    public ServerProcess Start(string instanceId, ServerLaunch launch, Action<ServerProcess>? beforeStart = null)
    {
        lock (_gate)
        {
            if (Current is { IsRunning: true })
            {
                throw new ServerStartException(ServerStartProblem.AnotherServerRunning);
            }

            var process = new ServerProcess(launch);
            beforeStart?.Invoke(process);
            process.Start();

            Current?.Dispose();
            Current = process;
            CurrentInstanceId = instanceId;
            return process;
        }
    }
}
