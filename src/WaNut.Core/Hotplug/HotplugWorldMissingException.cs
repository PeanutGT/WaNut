namespace WaNut.Core.Hotplug;

/// <summary>The server has not made its world folder yet, so there is nothing to hot-plug into.</summary>
public sealed class HotplugWorldMissingException(string worldFolder)
    : Exception($"The world folder '{worldFolder}' doesn't exist on the VPS yet. Start the server once first.")
{
    public string WorldFolder { get; } = worldFolder;
}
