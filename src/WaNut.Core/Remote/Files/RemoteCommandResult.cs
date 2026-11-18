namespace WaNut.Core.Remote.Files;

/// <summary>How a command on the VPS ended: its exit status and what it printed.</summary>
public sealed record RemoteCommandResult(int ExitStatus, string Output, string Error);
