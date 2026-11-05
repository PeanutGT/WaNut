using System.Globalization;
using System.Text;
using Renci.SshNet;
using Renci.SshNet.Common;
using WaNut.Core.Hosting;

namespace WaNut.Core.Remote.Run;

/// <summary>
/// One run of a server on the user's VPS. <see cref="ServerLaunch.FileName"/> and
/// <see cref="ServerLaunch.WorkingDirectory"/> are paths under the SSH user's home folder. The console is read with
/// tail over SSH, commands go into the runner's FIFO, and memory and CPU come from /proc every two seconds. If the
/// connection drops, WaNut tries to get it back for a while and carries on reading where it stopped.
/// With <paramref name="runnerPid"/> it follows a server already running there (started before WaNut was last closed)
/// instead of starting one, and reads its console from the beginning so the status and players are right.
/// </summary>
public sealed class RemoteServer(ServerLaunch launch, Func<CancellationToken, Task<SshClient>> connect, int? runnerPid = null) : RunningServerBase
{
    /// <summary>How often a lost connection is tried again, and how long to wait between tries.</summary>
    public static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    public const int ReconnectTries = 6;

    private static readonly TimeSpan UsageEvery = TimeSpan.FromSeconds(2);

    private readonly CancellationTokenSource _stop = new();
    private SshClient? _client;
    private Task? _run;
    private int _runnerPid;
    private ProcessUsage? _usage;
    private int _cores = 1;

    public ServerLaunch Launch { get; } = launch;

    /// <summary>The line written to the console when the connection could not be brought back.</summary>
    public const string LostConnectionLine = "[WaNut] Lost the connection to the VPS. The server may still be running there.";

    /// <summary>True when the run ended because the VPS could no longer be reached, not because the server stopped.</summary>
    public bool ConnectionLost { get; private set; }

    /// <summary>True when WaNut let go of the server with <see cref="Detach"/>: it keeps running on the VPS.</summary>
    public bool Detached { get; private set; }

    /// <summary>True when this run follows a server that was already running instead of starting one.</summary>
    public bool Reattached => runnerPid is not null;

    public override int ProcessorCount => _cores;

    /// <summary>The systemd service the server runs as, or null when it runs as a plain background process.</summary>
    public string? UnitName { get; private set; }

    protected override bool HasStarted => _run is not null;

    private string Root => Launch.WorkingDirectory;

    public override void Start()
    {
        if (_run is not null)
        {
            throw new InvalidOperationException("This server was already started.");
        }

        SetStatus(ServerStatus.Starting);
        _run = RunAsync(_stop.Token);
    }

    public override async Task SendCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        if (_client is not { IsConnected: true } client || !IsRunning)
        {
            throw new InvalidOperationException("The server is not running.");
        }

        if (PrepareCommand(command) is not { } line)
        {
            return;
        }

        try
        {
            await VpsConnector.RunWithInputAsync(client, RunnerScript.SendCommand(Root), line + "\n", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is SshException or System.Net.Sockets.SocketException)
        {
            throw new IOException(e.Message, e);
        }
    }

    /// <summary>The latest reading from the VPS. Zero until the first one arrives, so a watcher keeps watching.</summary>
    public override ProcessUsage? GetUsage() => Exited.IsCompleted ? null : _usage ?? new ProcessUsage(0, TimeSpan.Zero);

    public override void Kill()
    {
        WasKilled = true;
        if (_client is { IsConnected: true } client)
        {
            _ = VpsConnector.RunAsync(client, RunnerScript.KillCommand(Root)).ContinueWith(
                t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        }
    }

    /// <summary>
    /// Stops following the server without stopping it, for when WaNut closes (plan decision 5A). The run then ends
    /// here as a requested stop, so nothing reads it as a crash or brings the folder back.
    /// </summary>
    public void Detach()
    {
        if (Exited.IsCompleted)
        {
            return;
        }

        Detached = true;
        StopRequested = true;
        _stop.Cancel();
    }

    public override void Dispose()
    {
        _stop.Cancel();
        _client?.Dispose();
        _stop.Dispose();
    }

    /// <summary>Splits raw console bytes into lines and counts how many bytes were consumed, for resuming.</summary>
    public sealed class LineSplitter
    {
        private readonly MemoryStream _pending = new();

        public long Consumed { get; set; }

        public IEnumerable<string> Push(ReadOnlySpan<byte> bytes)
        {
            var lines = new List<string>();
            foreach (var b in bytes)
            {
                if (b == (byte)'\n')
                {
                    var line = Encoding.UTF8.GetString(_pending.GetBuffer(), 0, (int)_pending.Length).TrimEnd('\r');
                    Consumed += _pending.Length + 1;
                    _pending.SetLength(0);
                    lines.Add(line);
                }
                else
                {
                    _pending.WriteByte(b);
                }
            }

            return lines;
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var exitCode = -1;
        try
        {
            _client = await connect(cancellationToken).ConfigureAwait(false);
            if (runnerPid is { } running)
            {
                _runnerPid = running;
            }
            else if (!int.TryParse((await LaunchAsync(_client, cancellationToken).ConfigureAwait(false)).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out _runnerPid))
            {
                throw new InvalidOperationException("The VPS did not start the server.");
            }

            _ = WatchUsageAsync(cancellationToken);
            exitCode = await FollowAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            // Detached or disposed while running: whatever the cut connection threw, the server is not ours to judge.
        }
        catch (Exception e) when (e is SshException or IOException or InvalidOperationException or VpsUnreachableException or VpsLoginException or HostKeyException or System.Net.Sockets.SocketException)
        {
            OnLine($"[WaNut] {e.Message}");
        }

        OnExited(exitCode);
    }

    /// <summary>
    /// Starts the server: as a systemd service when the account has password-less sudo (the server then also saves on a
    /// VPS shutdown), otherwise as a detached background process. Returns the runner's pid.
    /// </summary>
    private async Task<string> LaunchAsync(SshClient client, CancellationToken cancellationToken)
    {
        await VpsConnector.RunWithInputAsync(client, RunnerScript.WriteStartScriptCommand(Root), RunnerScript.StartScript(Root, Launch.FileName, Launch.Arguments), cancellationToken).ConfigureAwait(false);
        var mode = (await VpsConnector.RunAsync(client, RunnerScript.ModeCommand, cancellationToken).ConfigureAwait(false)).Split('\n');
        if (mode.Length >= 3 && mode[0].Trim() == "systemd" && RunnerScript.IsPlainUserName(mode[2].Trim()) && mode[1].Trim().StartsWith('/'))
        {
            var (home, user) = (mode[1].Trim(), mode[2].Trim());
            UnitName = RunnerScript.UnitName(user, Root);
            return await VpsConnector.RunWithInputAsync(client, RunnerScript.StartUnitCommand(UnitName, Root), RunnerScript.Unit(home, user, Root), cancellationToken).ConfigureAwait(false);
        }

        return await VpsConnector.RunAsync(client, RunnerScript.LaunchPlainCommand(Root), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads the console until the server ends; returns its exit code.</summary>
    private async Task<int> FollowAsync(CancellationToken cancellationToken)
    {
        var splitter = new LineSplitter();
        var failures = 0;
        while (true)
        {
            try
            {
                var client = _client!;
                if (!client.IsConnected)
                {
                    _client = client = await connect(cancellationToken).ConfigureAwait(false);
                }

                await TailAsync(client, splitter, cancellationToken).ConfigureAwait(false);
                var state = (await VpsConnector.RunAsync(client, RunnerScript.StateCommand(Root, _runnerPid), cancellationToken).ConfigureAwait(false)).Trim();
                failures = 0;
                if (state == "running")
                {
                    continue;
                }

                return int.TryParse(state, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var code) ? code : -1;
            }
            catch (Exception e) when (!cancellationToken.IsCancellationRequested && e is SshException or IOException or VpsUnreachableException or ObjectDisposedException or System.Net.Sockets.SocketException)
            {
                if (++failures > ReconnectTries)
                {
                    ConnectionLost = true;
                    StopRequested = true; // Not a crash: nothing is known about the server, so nothing restarts it here.
                    OnLine(LostConnectionLine);
                    return -1;
                }

                _client?.Dispose();
                await Task.Delay(ReconnectDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task TailAsync(SshClient client, LineSplitter splitter, CancellationToken cancellationToken)
    {
        using var tail = client.CreateCommand(RunnerScript.TailCommand(Root, _runnerPid, splitter.Consumed));
        var running = tail.ExecuteAsync(cancellationToken);
        var buffer = new byte[16 * 1024];
        var output = tail.OutputStream;
        int read;
        while ((read = await output.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            foreach (var line in splitter.Push(buffer.AsSpan(0, read)))
            {
                OnLine(line);
            }
        }

        await running.ConfigureAwait(false);
    }

    private async Task WatchUsageAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(UsageEvery);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false) && !Exited.IsCompleted)
            {
                if (_client is not { IsConnected: true } client)
                {
                    continue;
                }

                try
                {
                    var usage = ParseUsage(await VpsConnector.RunAsync(client, RunnerScript.UsageCommand(Root), cancellationToken).ConfigureAwait(false));
                    if (usage is not null)
                    {
                        (_usage, _cores) = usage.Value;
                    }
                }
                catch (Exception e) when (e is SshException or InvalidOperationException or IOException or ObjectDisposedException)
                {
                    // java may not be up yet, or the connection is being brought back; the next tick tries again.
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Disposed.
        }
    }

    /// <summary>Reads <see cref="RunnerScript.UsageCommand"/>'s four lines.</summary>
    public static (ProcessUsage Usage, int Cores)? ParseUsage(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 4
            || !long.TryParse(lines[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || !long.TryParse(lines[1], NumberStyles.None, CultureInfo.InvariantCulture, out var rssKb)
            || !long.TryParse(lines[2], NumberStyles.None, CultureInfo.InvariantCulture, out var perSecond) || perSecond <= 0
            || !int.TryParse(lines[3], NumberStyles.None, CultureInfo.InvariantCulture, out var cores) || cores <= 0)
        {
            return null;
        }

        return (new ProcessUsage(rssKb * 1024, TimeSpan.FromSeconds((double)ticks / perSecond)), cores);
    }
}
