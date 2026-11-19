namespace WaNut.Core.Hotplug.Readiness;

/// <summary>A file in the inbox, as one look at the folder saw it. Two looks that agree on size and time mean it isn't being written.</summary>
public sealed record ReadinessEntry(string Name, long SizeBytes, DateTime ModifiedUtc);
