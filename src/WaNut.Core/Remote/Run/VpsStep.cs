namespace WaNut.Core.Remote.Run;

/// <summary>What WaNut is doing on the VPS before a start, for the status line.</summary>
public enum VpsStep
{
    Connecting,
    PreparingJava,
    Uploading,
}
