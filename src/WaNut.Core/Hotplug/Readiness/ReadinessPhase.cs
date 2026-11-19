namespace WaNut.Core.Hotplug.Readiness;

public enum ReadinessPhase
{
    /// <summary>No file yet, or the sentinel or a file the manifest names is missing.</summary>
    WaitingForData,

    /// <summary>Files are there but one is still changing or held open.</summary>
    Settling,

    /// <summary>Everything is stable; counting down the debounce window.</summary>
    Debouncing,
}
