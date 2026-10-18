namespace WaNut.Core.Hosting;

/// <param name="CpuPercent">Share of the whole PC, as <see cref="CpuMeter"/> gives it.</param>
public readonly record struct UsageSample(DateTimeOffset At, double CpuPercent, long MemoryBytes);

/// <summary>How hard the server works the PC, for the peanut on the Performance tab.</summary>
public enum UsageLoad
{
    Calm,
    Busy,
    Hot,
}

/// <summary>The running server's CPU and memory over the last <see cref="Span"/>, for the Performance tab.</summary>
public sealed class UsageHistory
{
    public static readonly TimeSpan Span = TimeSpan.FromMinutes(10);

    /// <summary>CPU share of the whole PC from which the peanut starts to sweat, and from which it overheats.</summary>
    public const double BusyCpu = 40;

    public const double HotCpu = 75;

    private readonly List<UsageSample> _samples = [];

    public IReadOnlyList<UsageSample> Samples => _samples;

    public UsageSample? Latest => _samples.Count > 0 ? _samples[^1] : null;

    public double PeakCpu => _samples.Count > 0 ? _samples.Max(s => s.CpuPercent) : 0;

    public long PeakMemory => _samples.Count > 0 ? _samples.Max(s => s.MemoryBytes) : 0;

    /// <summary>Adds a sample and drops the ones older than <see cref="Span"/> before it.</summary>
    public void Add(UsageSample sample)
    {
        _samples.Add(sample);
        var oldest = sample.At - Span;
        var old = _samples.FindIndex(s => s.At >= oldest);
        if (old > 0)
        {
            _samples.RemoveRange(0, old);
        }
    }

    public void Clear() => _samples.Clear();

    public static UsageLoad LoadOf(double cpuPercent) => cpuPercent switch
    {
        >= HotCpu => UsageLoad.Hot,
        >= BusyCpu => UsageLoad.Busy,
        _ => UsageLoad.Calm,
    };
}
