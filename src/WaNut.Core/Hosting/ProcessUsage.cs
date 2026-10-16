namespace WaNut.Core.Hosting;

/// <summary>What the server's java.exe uses right now: memory as Windows counts it, and CPU time so far.</summary>
/// <param name="MemoryBytes">The process's working set. It includes Java's own overhead, so it can exceed the RAM setting.</param>
/// <param name="CpuTime">Total processor time used since the process started, on all cores.</param>
public sealed record ProcessUsage(long MemoryBytes, TimeSpan CpuTime);
