namespace WaNut.Core.Hotplug.Preflight;

/// <summary>Whether a data pack's pack.mcmeta says it was made for the server's Minecraft version.</summary>
public enum FormatCheck
{
    Matches,

    /// <summary>Made for another version: Minecraft still loads it, but parts may fail.</summary>
    Mismatch,

    /// <summary>The server's version is newer than WaNut's table, or the pack names no format.</summary>
    Unknown,
}
