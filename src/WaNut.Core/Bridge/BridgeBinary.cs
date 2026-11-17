namespace WaNut.Core.Bridge;

/// <summary>A bridge executable on this PC, ready to run here or to upload.</summary>
/// <param name="Rid">The .NET runtime id it was built for.</param>
/// <param name="Path">The file on this PC.</param>
/// <param name="Sha256">Lowercase hex, so a VPS copy can be compared without uploading again.</param>
public sealed record BridgeBinary(string Rid, string Path, string Sha256);
