using WaNut.Core.Hotplug.Preflight;

namespace WaNut.Core.Hotplug;

/// <summary>One batch to hot-plug: zips on this PC to add or update, and names in the datapacks folder to take out.</summary>
/// <param name="Add">Paths of .zip files on this PC. A pack already on the server under the same name is replaced.</param>
/// <param name="Remove">File names in the world's datapacks folder.</param>
/// <param name="MinecraftVersion">The server's version, for the pack_format check.</param>
/// <param name="Confirm">
/// Asked when a pack is for another version or has parts that only apply after a restart; false stops the run.
/// Null goes ahead.
/// </param>
public sealed record HotplugRequest(
    IReadOnlyList<string> Add,
    IReadOnlyList<string> Remove,
    string MinecraftVersion,
    Func<IReadOnlyList<DatapackInspection>, CancellationToken, Task<bool>>? Confirm = null);
