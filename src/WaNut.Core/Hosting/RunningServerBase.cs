namespace WaNut.Core.Hosting;

/// <summary>
/// What every <see cref="IRunningServer"/> does with the console, wherever the server runs: status from the log,
/// the players online, and the end of the run. Events are raised on background threads.
/// </summary>
public abstract class RunningServerBase : IRunningServer
{
    private readonly Lock _gate = new();
    private readonly HashSet<string> _players = new(StringComparer.OrdinalIgnoreCase);
    private readonly TaskCompletionSource<int> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public event Action<string>? OutputReceived;

    public event Action<ServerStatus>? StatusChanged;

    public event Action? PlayersChanged;

    public ServerStatus Status { get; private set; } = ServerStatus.Stopped;

    public Task<int> Exited => _exited.Task;

    public bool IsRunning => Status != ServerStatus.Stopped;

    public bool HasExited { get; private set; }

    public bool WasKilled { get; protected set; }

    public bool StopRequested { get; protected set; }

    public IReadOnlyList<string> Players
    {
        get
        {
            lock (_gate)
            {
                return _players.Order(StringComparer.OrdinalIgnoreCase).ToList();
            }
        }
    }

    /// <summary>The cores the server's machine has, so CPU time can be shown as a share of all of them.</summary>
    public virtual int ProcessorCount => Environment.ProcessorCount;

    public abstract void Start();

    public abstract Task SendCommandAsync(string command, CancellationToken cancellationToken = default);

    public abstract ProcessUsage? GetUsage();

    public abstract void Kill();

    public abstract void Dispose();

    /// <summary>Whether the server has been started at all; <see cref="StopAsync"/> has nothing to stop before that.</summary>
    protected abstract bool HasStarted { get; }

    public async Task<bool> StopAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (!HasStarted || Exited.IsCompleted)
        {
            return true;
        }

        if (Status != ServerStatus.Stopping)
        {
            SetStatus(ServerStatus.Stopping);
            try
            {
                await SendCommandAsync("stop", cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is IOException or InvalidOperationException)
            {
                // The server ended or closed its input on its own; the exit wait below settles it.
            }
        }

        try
        {
            await Exited.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    /// <summary>One line for the console, or null when there is no line to send. Marks a typed "stop".</summary>
    protected string? PrepareCommand(string command)
    {
        // One line per command: a pasted newline must not turn into a second command.
        var line = command.ReplaceLineEndings(" ").Trim();
        if (line.Length == 0)
        {
            return null;
        }

        if (line.Equals("stop", StringComparison.OrdinalIgnoreCase))
        {
            StopRequested = true;
        }

        return line;
    }

    protected void OnLine(string? data)
    {
        if (data is null)
        {
            return;
        }

        var line = ServerLogParser.Clean(data);
        OutputReceived?.Invoke(line);

        var logEvent = ServerLogParser.Parse(line);
        switch (logEvent?.Kind)
        {
            case LogEventKind.Online:
                SetStatus(ServerStatus.Online, onlyFrom: ServerStatus.Starting);
                break;
            case LogEventKind.Stopping:
                SetStatus(ServerStatus.Stopping, onlyFrom: null);
                break;
            case LogEventKind.PlayerJoined:
                UpdatePlayers(set => set.Add(logEvent.PlayerName!));
                break;
            case LogEventKind.PlayerLeft:
                UpdatePlayers(set => set.Remove(logEvent.PlayerName!));
                break;
        }
    }

    /// <summary>Ends the run: players are cleared, the status goes to Stopped and <see cref="Exited"/> completes.</summary>
    protected void OnExited(int exitCode)
    {
        HasExited = true;

        bool hadPlayers;
        lock (_gate)
        {
            hadPlayers = _players.Count > 0;
            _players.Clear();
        }

        if (hadPlayers)
        {
            PlayersChanged?.Invoke();
        }

        SetStatus(ServerStatus.Stopped);
        _exited.TrySetResult(exitCode);
    }

    protected void SetStatus(ServerStatus status, ServerStatus? onlyFrom = null)
    {
        lock (_gate)
        {
            // Once the server has exited, nothing brings the status back.
            if (Status == status || _exited.Task.IsCompleted || (onlyFrom is not null && Status != onlyFrom))
            {
                return;
            }

            Status = status;
        }

        StatusChanged?.Invoke(status);
    }

    private void UpdatePlayers(Func<HashSet<string>, bool> change)
    {
        bool changed;
        lock (_gate)
        {
            changed = change(_players);
        }

        if (changed)
        {
            PlayersChanged?.Invoke();
        }
    }
}
