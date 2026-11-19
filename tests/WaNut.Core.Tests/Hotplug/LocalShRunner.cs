using System.Diagnostics;
using WaNut.Core.Remote.Files;

namespace WaNut.Core.Tests.Hotplug;

/// <summary>Runs the VPS commands on this machine's sh, for tests where no SSH server is available.</summary>
internal sealed class LocalShRunner : IRemoteCommandRunner
{
    public async Task<RemoteCommandResult> RunAsync(string command, string? input = null, CancellationToken cancellationToken = default)
    {
        using var process = Process.Start(new ProcessStartInfo("/bin/sh") { ArgumentList = { "-c", command }, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true })!;
        await process.StandardInput.WriteAsync(input ?? string.Empty);
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return new RemoteCommandResult(process.ExitCode, await output, await error);
    }
}
