using System.Diagnostics;
using System.Text;

namespace WaNut.Core.Hosting;

/// <summary>
/// One running Minecraft server: a hidden java.exe whose console is streamed to WaNut.
/// Events are raised on background threads.
/// </summary>
public sealed class ServerProcess(ServerLaunch launch) : IDisposable
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _inputLock = new(1, 1);
    private readonly HashSet<string> _players = new(StringComparer.OrdinalIgnoreCase);
    private readonly TaskCompletionSource<int> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Process? _process;

    public event Action<string>? OutputReceived;

    public event Action<ServerStatus>? StatusChanged;

    public event Action? PlayersChanged;

    public ServerLaunch Launch { get; } = launch;

    public ServerStatus Status { get; private set; } = ServerStatus.Stopped;

    /// <summary>Completes with the exit code once the process has ended and all its output has been read.</summary>
    public Task<int> Exited => _exited.Task;

    public bool IsRunning => Status != ServerStatus.Stopped;

    /// <summary>Whether Windows will end the process if WaNut itself crashes.</summary>
    public bool IsTrackedByJob { get; private set; }

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

    public void Start()
    {
        if (_process is not null)
        {
            throw new InvalidOperationException("This server process was already started.");
        }

        var info = new ProcessStartInfo(Launch.FileName)
        {
            WorkingDirectory = Launch.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Utf8,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8,
        };
        foreach (var argument in Launch.Arguments)
        {
            info.ArgumentList.Add(argument);
        }

        var process = new Process { StartInfo = info };
        process.OutputDataReceived += (_, e) => OnLine(e.Data);
        process.ErrorDataReceived += (_, e) => OnLine(e.Data);

        SetStatus(ServerStatus.Starting);
        try
        {
            process.Start();
        }
        catch
        {
            process.Dispose();
            SetStatus(ServerStatus.Stopped);
            throw;
        }

        _process = process;
        IsTrackedByJob = ProcessJob.Track(process);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _ = WatchExitAsync(process);
    }

    /// <summary>Sends one console command, e.g. "say hi" or "op Steve".</summary>
    public async Task SendCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        var process = _process;
        if (process is null || !IsRunning)
        {
            throw new InvalidOperationException("The server is not running.");
        }

        // One line per command: a pasted newline must not turn into a second command.
        var line = command.ReplaceLineEndings(" ").Trim();
        if (line.Length == 0)
        {
            return;
        }

        await _inputLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await process.StandardInput.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _inputLock.Release();
        }
    }

    /// <summary>
    /// Asks the server to save the world and exit, the same as typing "stop". Returns false if it is still running
    /// after <paramref name="timeout"/>; the caller then asks the user before calling <see cref="Kill"/>.
    /// </summary>
    public async Task<bool> StopAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (_process is null || Exited.IsCompleted)
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
                // The process ended or closed its input on its own; the exit wait below settles it.
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

    /// <summary>Ends the process without saving. Only for when the user agreed after a stop timed out.</summary>
    public void Kill()
    {
        try
        {
            _process?.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
    }

    public void Dispose()
    {
        _process?.Dispose();
        _inputLock.Dispose();
    }

    private async Task WatchExitAsync(Process process)
    {
        // Also waits until the redirected output has been read to the end.
        await process.WaitForExitAsync().ConfigureAwait(false);
        var exitCode = process.ExitCode;

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

    private void OnLine(string? data)
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

    private void SetStatus(ServerStatus status, ServerStatus? onlyFrom = null)
    {
        lock (_gate)
        {
            // Once the process has exited, nothing brings the status back.
            if (Status == status || _exited.Task.IsCompleted || (onlyFrom is not null && Status != onlyFrom))
            {
                return;
            }

            Status = status;
        }

        StatusChanged?.Invoke(status);
    }
}
