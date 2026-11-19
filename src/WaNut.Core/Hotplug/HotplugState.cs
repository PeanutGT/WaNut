namespace WaNut.Core.Hotplug;

/// <summary>Where a datapack hot-plug is. The last three are where a run ends.</summary>
public enum HotplugState
{
    Idle,

    /// <summary>Waiting for the files to be complete (<see cref="Readiness.ReadinessGate"/>).</summary>
    WaitingForData,

    /// <summary>Reading pack.mcmeta and the data folders on this PC.</summary>
    PreFlightCheck,

    /// <summary>Uploading to the staging folder as .part files.</summary>
    UploadingStaging,

    /// <summary>Comparing the VPS's SHA-256 with this PC's.</summary>
    VerifyingRemote,

    /// <summary>Renaming the checked files into the world's datapacks folder.</summary>
    AtomicSwap,

    /// <summary>/reload, then checking that the packs are enabled.</summary>
    ApplyingAndReloading,

    /// <summary>Applied and checked.</summary>
    Completed,

    /// <summary>Something went wrong after the swap; the old files are back.</summary>
    RolledBack,

    /// <summary>Stopped before anything in the world changed: refused, cancelled, or the upload failed.</summary>
    Aborted,
}
