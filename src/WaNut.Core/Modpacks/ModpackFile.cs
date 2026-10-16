using WaNut.Core.Downloads;

namespace WaNut.Core.Modpacks;

/// <summary>One file the pack downloads into the server folder.</summary>
/// <param name="Path">Relative to the server folder, with forward slashes, e.g. "mods/lithium.jar".</param>
public sealed record ModpackFile(string Path, Uri Url, ExpectedHash Hash);
