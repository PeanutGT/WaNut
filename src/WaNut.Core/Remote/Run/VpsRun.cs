using System.Globalization;

namespace WaNut.Core.Remote.Run;

/// <summary>What <see cref="RunnerScript.ProbeCommand"/> found for a server on the VPS.</summary>
public enum VpsRunState
{
    /// <summary>Nothing ran there, or what ran left no trace: there is nothing to reconnect to.</summary>
    None,

    /// <summary>The server is still running there; <see cref="VpsRun.RunnerPid"/> says which runner to follow.</summary>
    Running,

    /// <summary>The server ended while WaNut was away; its files can come back.</summary>
    Ended,
}

/// <param name="RunnerPid">The runner's pid while <see cref="VpsRunState.Running"/>.</param>
/// <param name="ExitCode">The server's exit code once <see cref="VpsRunState.Ended"/>, -1 when unknown.</param>
public sealed record VpsRun(VpsRunState State, int RunnerPid = 0, int ExitCode = -1)
{
    public static VpsRun Parse(string output)
    {
        var words = output.Trim().Split(' ', 2, StringSplitOptions.TrimEntries);
        return words switch
        {
            ["running", var pid] when int.TryParse(pid, NumberStyles.None, CultureInfo.InvariantCulture, out var p) && p > 1 => new VpsRun(VpsRunState.Running, p),
            ["exit", var code] => new VpsRun(VpsRunState.Ended, ExitCode: int.TryParse(code, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var c) ? c : -1),
            ["exit"] => new VpsRun(VpsRunState.Ended),
            _ => new VpsRun(VpsRunState.None),
        };
    }
}
