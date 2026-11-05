using System.Diagnostics;
using System.Text;

namespace WaNut.Core.Hosting;

/// <summary>
/// One running Minecraft server: a hidden java.exe whose console is streamed to WaNut.
/// Events are raised on background threads.
/// </summary>
public sealed class ServerProcess(ServerLaunch launch) : RunningServerBase
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly SemaphoreSlim _inputLock = new(1, 1);
    private Process? _process;

    public ServerLaunch Launch { get; } = launch;

    /// <summary>Whether Windows will end the process if WaNut itself crashes.</summary>
    public bool IsTrackedByJob { get; private set; }

    protected override bool HasStarted => _process is not null;

    public override void Start()
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
    public override async Task SendCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        var process = _process;
        if (process is null || !IsRunning)
        {
            throw new InvalidOperationException("The server is not running.");
        }

        if (PrepareCommand(command) is not { } line)
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

    /// <summary>The process's current memory and CPU time, or null when it is not running or Windows won't say.</summary>
    public override ProcessUsage? GetUsage()
    {
        var process = _process;
        if (process is null || Exited.IsCompleted)
        {
            return null;
        }

        try
        {
            process.Refresh();
            return new ProcessUsage(process.WorkingSet64, process.TotalProcessorTime);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // Exited between the check and the read.
            return null;
        }
    }

    /// <summary>Ends the process without saving. Only for when the user agreed after a stop timed out.</summary>
    public override void Kill()
    {
        WasKilled = true;
        try
        {
            _process?.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
    }

    public override void Dispose()
    {
        _process?.Dispose();
        _inputLock.Dispose();
    }

    private async Task WatchExitAsync(Process process)
    {
        // Also waits until the redirected output has been read to the end.
        await process.WaitForExitAsync().ConfigureAwait(false);
        OnExited(process.ExitCode);
    }
}
