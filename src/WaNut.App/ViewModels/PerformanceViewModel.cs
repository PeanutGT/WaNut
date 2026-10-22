using CommunityToolkit.Mvvm.ComponentModel;
using WaNut.App.Resources;
using WaNut.Core.Hosting;

namespace WaNut.App.ViewModels;

/// <summary>
/// The Performance tab: the running server's CPU and memory over the last ten minutes, and a peanut that is calm,
/// sweating or overheating with the CPU load.
/// </summary>
public sealed partial class PerformanceViewModel(ServerViewModel server) : ObservableObject
{
    private const double Gigabyte = 1024.0 * 1024 * 1024;

    private readonly UsageHistory _history = new();

    /// <summary>False before the first reading of a running server.</summary>
    public bool HasData => _history.Latest is not null;

    public bool HasNoData => !HasData;

    internal UsageSample? Latest => _history.Latest;

    public string Scene => _history.Latest is { } latest ? $"perf-{UsageHistory.LoadOf(latest.CpuPercent).ToString().ToLowerInvariant()}" : "stopped";

    public string? MoodText => _history.Latest is { } latest ? Text.Get($"Performance_Mood_{UsageHistory.LoadOf(latest.CpuPercent)}") : null;

    public string? CpuText => _history.Latest is { } latest ? string.Format(Text.Get("Performance_CpuValue"), latest.CpuPercent) : null;

    public string? CpuPeakText => HasData ? string.Format(Text.Get("Performance_Peak"), string.Format(Text.Get("Performance_CpuValue"), _history.PeakCpu)) : null;

    public string? MemoryText => _history.Latest is { } latest
        ? string.Format(Text.Get("Performance_MemoryValue"), latest.MemoryBytes / Gigabyte, AllocatedBytes / Gigabyte)
        : null;

    public string? MemoryPeakText => HasData ? string.Format(Text.Get("Performance_Peak"), string.Format(Text.Get("Performance_Gigabytes"), _history.PeakMemory / Gigabyte)) : null;

    /// <summary>
    /// "2 servers together: CPU 35% · 7.2 GB": every running server added up, shown while more than one runs.
    /// </summary>
    public string? TotalText
    {
        get
        {
            var latest = server.RunningServers.Select(s => s.Performance.Latest).OfType<UsageSample>().ToList();
            return latest.Count < 2
                ? null
                : string.Format(Text.Get("Performance_Total"), latest.Count, latest.Sum(s => s.CpuPercent), latest.Sum(s => s.MemoryBytes) / Gigabyte);
        }
    }

    public bool HasTotal => TotalText is not null;

    public IReadOnlyList<double> CpuValues => [.. _history.Samples.Select(s => s.CpuPercent / 100)];

    /// <summary>Memory as a share of the chart's top: the RAM set for the server, or more if Java went past it.</summary>
    public IReadOnlyList<double> MemoryValues
    {
        get
        {
            var top = Math.Max(AllocatedBytes, _history.PeakMemory) * 1.1;
            return [.. _history.Samples.Select(s => top > 0 ? s.MemoryBytes / top : 0)];
        }
    }

    private double AllocatedBytes => server.Metadata.MemoryMb * 1024.0 * 1024;

    internal void Add(UsageSample sample)
    {
        _history.Add(sample);
        Refresh();
    }

    /// <summary>A new run starts with an empty chart.</summary>
    internal void Clear()
    {
        _history.Clear();
        Refresh();
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(HasData));
        OnPropertyChanged(nameof(HasNoData));
        OnPropertyChanged(nameof(Scene));
        OnPropertyChanged(nameof(MoodText));
        OnPropertyChanged(nameof(CpuText));
        OnPropertyChanged(nameof(CpuPeakText));
        OnPropertyChanged(nameof(MemoryText));
        OnPropertyChanged(nameof(MemoryPeakText));
        OnPropertyChanged(nameof(CpuValues));
        OnPropertyChanged(nameof(MemoryValues));
        OnPropertyChanged(nameof(TotalText));
        OnPropertyChanged(nameof(HasTotal));
    }
}
