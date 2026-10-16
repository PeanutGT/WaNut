namespace WaNut.Core.Hosting;

/// <summary>
/// Turns CPU time samples into a percentage of the whole PC, the way Task Manager shows it: a server using one
/// core fully on an 8-core PC is 12.5%.
/// </summary>
public sealed class CpuMeter(int processorCount)
{
    private TimeSpan? _lastCpu;
    private DateTimeOffset _lastAt;

    /// <summary>Records a sample. Returns the percentage since the previous one, or null for the first sample.</summary>
    public double? Sample(TimeSpan cpuTime, DateTimeOffset now)
    {
        var previous = _lastCpu;
        var elapsed = now - _lastAt;
        _lastCpu = cpuTime;
        _lastAt = now;
        if (previous is null || elapsed <= TimeSpan.Zero)
        {
            return null;
        }

        var percent = (cpuTime - previous.Value) / (elapsed * Math.Max(1, processorCount)) * 100;
        return Math.Clamp(percent, 0, 100);
    }
}
