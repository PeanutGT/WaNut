using System.Diagnostics;
using WaNut.Core.Hosting;

namespace WaNut.Core.Tunnels;

/// <summary>
/// Runs WaNut's playit agent in the background, without a window, while a server with the tunnel on is running. It is
/// in WaNut's Job Object, so it ends with WaNut even after a crash.
/// </summary>
public sealed class PlayitAgentProcess(PlayitFiles files) : IDisposable
{
    // Its own pipe, so it never talks to a playit program the user installed separately.
    public const string PipeName = @"\\.\pipe\wanut-playitd";

    private Process? _process;

    public bool IsRunning => _process is { HasExited: false };

    public static IReadOnlyList<string> Arguments(PlayitFiles files) =>
        ["--secret-path", files.SecretFile, "--socket-path", PipeName, "--log-path", files.LogFile];

    /// <summary>Starts the agent unless it already runs.</summary>
    public void Start(string agentExe)
    {
        if (IsRunning)
        {
            return;
        }

        _process?.Dispose();
        var info = new ProcessStartInfo(agentExe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = files.Dir,
        };
        foreach (var argument in Arguments(files))
        {
            info.ArgumentList.Add(argument);
        }

        _process = Process.Start(info) ?? throw new InvalidOperationException("The playit agent did not start.");
        ProcessJob.Track(_process);
    }

    /// <summary>
    /// Ends the agent. It holds no data that could be lost, so it is ended at once; friends' connections close and
    /// the tunnel's address stays the same for next time.
    /// </summary>
    public void Stop()
    {
        if (_process is null)
        {
            return;
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(5000);
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }

        _process.Dispose();
        _process = null;
    }

    public void Dispose() => Stop();
}
