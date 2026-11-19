using WaNut.Core.Hotplug.Readiness;

namespace WaNut.Core.Hotplug;

/// <param name="Detail">The file being worked on.</param>
/// <param name="Fraction">0 to 1 while uploading.</param>
/// <param name="Readiness">What the readiness gate is doing, while <see cref="HotplugState.WaitingForData"/>.</param>
public sealed record HotplugProgress(HotplugState State, string? Detail = null, double? Fraction = null, ReadinessStatus? Readiness = null);
