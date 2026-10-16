namespace WaNut.Core.Versions;

/// <summary>A Forge or NeoForge installer failed, or left no server to start.</summary>
public sealed class ForgeInstallException(int exitCode, string output)
    : Exception($"The loader installer failed (exit code {exitCode}).{Environment.NewLine}{output}")
{
    public int ExitCode { get; } = exitCode;

    /// <summary>The installer's last lines of output.</summary>
    public string Output { get; } = output;
}
