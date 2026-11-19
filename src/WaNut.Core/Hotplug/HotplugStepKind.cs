namespace WaNut.Core.Hotplug;

public enum HotplugStepKind
{
    /// <summary>A checked upload goes into the datapacks folder, as a new pack or over the old one.</summary>
    Put,

    /// <summary>A pack leaves the datapacks folder for the backup folder.</summary>
    Retire,
}
