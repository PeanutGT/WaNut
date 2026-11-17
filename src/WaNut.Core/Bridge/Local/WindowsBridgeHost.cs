using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;

namespace WaNut.Core.Bridge.Local;

/// <summary>Windows processes and the user's own Task Scheduler tasks, through CreateProcess and schtasks.exe.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsBridgeHost : ILocalBridgeHost
{
    private const uint CreateNoWindow = 0x08000000;
    private const uint CreateBreakawayFromJob = 0x01000000;
    private const uint CreateNewProcessGroup = 0x00000200;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const int ErrorAccessDenied = 5;

    public int Start(string executable, IReadOnlyList<string> arguments)
    {
        var commandLine = WindowsCommandLine.Join([executable, .. arguments]);

        // Out of any job WaNut runs in, so closing WaNut never ends the bridge; a job that forbids that still gets a bridge.
        if (TryCreate(executable, commandLine, CreateBreakawayFromJob, out var pid, out var error)
            || (error == ErrorAccessDenied && TryCreate(executable, commandLine, 0, out pid, out error)))
        {
            return pid;
        }

        throw new Win32Exception(error);
    }

    public bool IsRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited && process.ProcessName.Equals("WaNut.Bridge", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    public void Kill(int processId)
    {
        if (IsRunning(processId))
        {
            using var process = Process.GetProcessById(processId);
            process.Kill();
        }
    }

    public async Task RegisterTaskAsync(string taskName, string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
    {
        var xml = BridgeTaskXml.Build(WindowsIdentity.GetCurrent().Name, executable, WindowsCommandLine.Join(arguments), taskName);
        var file = Path.Combine(Path.GetTempPath(), $"wanut-bridge-{Guid.NewGuid():N}.xml");
        await File.WriteAllTextAsync(file, xml, Encoding.Unicode, cancellationToken).ConfigureAwait(false);
        try
        {
            await SchtasksAsync(["/Create", "/TN", taskName, "/XML", file, "/F"], cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            File.Delete(file);
        }

        await SchtasksAsync(["/Run", "/TN", taskName], cancellationToken).ConfigureAwait(false);
    }

    public Task EndTaskAsync(string taskName, CancellationToken cancellationToken = default) =>
        SchtasksAsync(["/End", "/TN", taskName], cancellationToken, ignoreFailure: true);

    public Task DeleteTaskAsync(string taskName, CancellationToken cancellationToken = default) =>
        SchtasksAsync(["/Delete", "/TN", taskName, "/F"], cancellationToken, ignoreFailure: true);

    private static async Task SchtasksAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken, bool ignoreFailure = false)
    {
        var start = new ProcessStartInfo("schtasks.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("schtasks.exe did not start.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (process.ExitCode != 0 && !ignoreFailure)
        {
            throw new InvalidOperationException($"schtasks {arguments[0]} failed ({process.ExitCode}): {(await error.ConfigureAwait(false)).Trim()} {(await output.ConfigureAwait(false)).Trim()}");
        }
    }

    private static bool TryCreate(string executable, string commandLine, uint extraFlags, out int processId, out int error)
    {
        var startup = new StartupInfo { cb = Marshal.SizeOf<StartupInfo>() };
        var line = new StringBuilder(commandLine);
        if (!CreateProcessW(executable, line, 0, 0, false, CreateNoWindow | CreateNewProcessGroup | CreateUnicodeEnvironment | extraFlags,
                0, Path.GetDirectoryName(executable), ref startup, out var info))
        {
            processId = 0;
            error = Marshal.GetLastPInvokeError();
            return false;
        }

        CloseHandle(info.hThread);
        CloseHandle(info.hProcess);
        processId = info.dwProcessId;
        error = 0;
        return true;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public nint lpReserved2;
        public nint hStdInput;
        public nint hStdOutput;
        public nint hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public nint hProcess;
        public nint hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcessW(
        string lpApplicationName,
        StringBuilder lpCommandLine,
        nint lpProcessAttributes,
        nint lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        nint lpEnvironment,
        string? lpCurrentDirectory,
        ref StartupInfo lpStartupInfo,
        out ProcessInformation lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint handle);
}
