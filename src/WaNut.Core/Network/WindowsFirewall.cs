using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace WaNut.Core.Network;

/// <summary>
/// Windows Defender Firewall through Windows PowerShell's NetSecurity module. Reading needs no administrator rights;
/// adding the rule runs PowerShell elevated, which shows one UAC prompt.
/// </summary>
public sealed class WindowsFirewall(LauncherPaths paths) : IFirewall
{
    /// <summary>
    /// When set to 1, AllowAsync writes the script it would run to firewall-dry-run.ps1 in the data folder instead
    /// of asking for administrator approval, so the flow can be tested without changing the firewall.
    /// </summary>
    public const string DryRunVariable = "WANUT_FIREWALL_DRY_RUN";

    public const string DryRunFileName = "firewall-dry-run.ps1";

    private const int ErrorCancelled = 1223;

    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(30);

    // Counted from after the UAC prompt was answered: adding the rules takes seconds.
    private static readonly TimeSpan ElevatedTimeout = TimeSpan.FromMinutes(2);

    public async Task<FirewallState> GetStateAsync(string program, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return FirewallState.Unknown;
        }

        using var process = new Process { StartInfo = PowerShell(FirewallScripts.ReadScript(program)) };
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.StandardOutputEncoding = Encoding.UTF8;
        try
        {
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errors = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).WaitAsync(ReadTimeout, cancellationToken).ConfigureAwait(false);
            await errors.ConfigureAwait(false);
            return process.ExitCode == 0 ? FirewallScripts.ParseState(await output.ConfigureAwait(false)) : FirewallState.Unknown;
        }
        catch (Exception e) when (e is Win32Exception or TimeoutException or InvalidOperationException)
        {
            TryKill(process);
            return FirewallState.Unknown;
        }
    }

    public async Task<FirewallChange> AllowAsync(string program, string ruleName, CancellationToken cancellationToken = default)
    {
        var script = FirewallScripts.AllowScript(program, ruleName);
        if (Environment.GetEnvironmentVariable(DryRunVariable) == "1")
        {
            Directory.CreateDirectory(paths.Root);
            await File.WriteAllTextAsync(Path.Combine(paths.Root, DryRunFileName), script, cancellationToken).ConfigureAwait(false);
            return FirewallChange.Declined;
        }

        if (!OperatingSystem.IsWindows())
        {
            return FirewallChange.Failed;
        }

        var start = PowerShell(script);
        start.UseShellExecute = true;
        start.Verb = "runas";
        start.WindowStyle = ProcessWindowStyle.Hidden;
        try
        {
            using var process = Process.Start(start);
            if (process is null)
            {
                return FirewallChange.Failed;
            }

            // A hung PowerShell must not leave WaNut waiting forever. The elevated process can't be killed from here,
            // so it is left to finish on its own; the Connect tab shows the real state later.
            try
            {
                await process.WaitForExitAsync(cancellationToken).WaitAsync(ElevatedTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                return FirewallChange.Failed;
            }

            if (process.ExitCode != 0)
            {
                return FirewallChange.Failed;
            }
        }
        catch (Win32Exception e) when (e.NativeErrorCode == ErrorCancelled)
        {
            return FirewallChange.Declined;
        }
        catch (Win32Exception)
        {
            return FirewallChange.Failed;
        }

        return await GetStateAsync(program, cancellationToken).ConfigureAwait(false) == FirewallState.Allowed
            ? FirewallChange.Done
            : FirewallChange.Failed;
    }

    private static ProcessStartInfo PowerShell(string script)
    {
        // By absolute path, so a powershell.exe elsewhere on PATH (or in the working folder) is never the one elevated.
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        return new ProcessStartInfo(exe)
        {
            ArgumentList = { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", FirewallScripts.Encode(script) },
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System),
        };
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill();
        }
        catch (InvalidOperationException)
        {
            // Never started or already exited.
        }
    }
}
