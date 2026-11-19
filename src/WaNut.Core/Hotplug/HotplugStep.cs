namespace WaNut.Core.Hotplug;

/// <summary>One move of a hot-plug. Paths are relative to the server folder.</summary>
/// <param name="FileName">The pack's file name in the datapacks folder.</param>
/// <param name="Staged">The checked upload in the staging folder; null for <see cref="HotplugStepKind.Retire"/>.</param>
/// <param name="Target">The pack's path in the world's datapacks folder.</param>
/// <param name="Backup">Where the old file goes: a time-stamped name in the backup folder.</param>
public sealed record HotplugStep(HotplugStepKind Kind, string FileName, string? Staged, string Target, string Backup);
