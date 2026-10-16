using System.Diagnostics;

namespace WaNut.Core.Versions;

/// <summary>
/// Runs a Forge or NeoForge installer in server mode ("--installServer"). The installer downloads Minecraft's server
/// jar and the loader's libraries itself, checking each against the hashes listed in the installer.
/// </summary>
public class ForgeInstallerRunner
{
    private const int KeptOutputLines = 15;

    /// <summary>Installs the server into <paramref name="targetDir"/>. Throws <see cref="ForgeInstallException"/> on failure.</summary>
    public virtual async Task RunAsync(string javaExe, string installerJar, string targetDir, CancellationToken cancellationToken = default)
    {
        var start = new ProcessStartInfo(javaExe)
        {
            WorkingDirectory = targetDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in new[] { "-jar", installerJar, "--installServer", targetDir })
        {
            start.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = start };
        var tail = new Queue<string>();
        void Keep(object sender, DataReceivedEventArgs e)
        {
            if (e.Data is null)
            {
                return;
            }

            lock (tail)
            {
                tail.Enqueue(e.Data);
                if (tail.Count > KeptOutputLines)
                {
                    tail.Dequeue();
                }
            }
        }

        process.OutputDataReceived += Keep;
        process.ErrorDataReceived += Keep;
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        if (process.ExitCode != 0)
        {
            lock (tail)
            {
                throw new ForgeInstallException(process.ExitCode, string.Join(Environment.NewLine, tail));
            }
        }
    }
}
