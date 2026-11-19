namespace WaNut.Core.Hotplug;

/// <summary>A data pack in the world's datapacks folder on the VPS.</summary>
/// <param name="Enabled">Whether the running server has it on; null when the console didn't say.</param>
public sealed record LiveDatapack(string FileName, bool IsFolder, long SizeBytes, DateTime ModifiedUtc, bool? Enabled)
{
    /// <summary>Folders and odd names can be seen but not hot-plugged.</summary>
    public bool CanHotplug => !IsFolder && HotplugNames.Problem(FileName) is null;
}
