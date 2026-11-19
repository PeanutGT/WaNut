namespace WaNut.Core.Hotplug.Readiness;

public enum ReadinessOutcome
{
    /// <summary>The batch is complete and stable.</summary>
    Ready,

    /// <summary>Not complete in time. Nothing should be applied.</summary>
    TimedOut,

    /// <summary>The manifest can't be used. Nothing should be applied.</summary>
    InvalidManifest,
}
