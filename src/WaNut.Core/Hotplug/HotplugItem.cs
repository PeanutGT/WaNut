namespace WaNut.Core.Hotplug;

/// <summary>A pack the run touched and what was done to it.</summary>
/// <param name="Sha256">The uploaded file's hash; null for a removal.</param>
public sealed record HotplugItem(string FileName, HotplugSwapResult Action, string? Sha256);
