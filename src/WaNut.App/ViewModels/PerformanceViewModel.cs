using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WaNut.App.Resources;
using WaNut.Core.Downloads;
using WaNut.Core.Hosting;
using WaNut.Core.Mods;
using WaNut.Core.Profiling;

namespace WaNut.App.ViewModels;

/// <summary>
/// The Performance tab: the running server's CPU and memory over the last ten minutes, and a peanut that is calm,
/// sweating or overheating with the CPU load. Below them, the spark profiler: install it, profile for a while and open
/// the report.
/// </summary>
public sealed partial class PerformanceViewModel(ServerViewModel server, Spark spark) : ObservableObject
{
    private const double Gigabyte = 1024.0 * 1024 * 1024;

    private readonly UsageHistory _history = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SparkHint))]
    [NotifyCanExecuteChangedFor(nameof(InstallSparkCommand))]
    private bool _isInstallingSpark;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SparkHint))]
    [NotifyCanExecuteChangedFor(nameof(ProfileCommand))]
    private bool _isProfiling;

    [ObservableProperty]
    private string? _sparkError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReport))]
    private Uri? _reportUrl;

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

    private SparkSource SparkSource => Spark.SourceFor(server.Metadata.Type, server.Metadata.MinecraftVersion);

    /// <summary>Vanilla loads neither mods nor plugins, so the spark card is hidden.</summary>
    public bool ShowSpark => SparkSource != SparkSource.None;

    public bool SparkBuiltIn => SparkSource == SparkSource.BuiltIn;

    public bool SparkInstalled => SafeIsSparkInstalled();

    public bool ShowSparkInstall => ShowSpark && !SparkInstalled;

    public bool ShowSparkProfile => ShowSpark && SparkInstalled;

    public bool HasReport => ReportUrl is not null;

    public string SparkProfileText => string.Format(Text.Get("Performance_SparkProfile"), Spark.ProfileSeconds);

    /// <summary>One line under the spark caption saying what to do next.</summary>
    public string SparkHint
    {
        get
        {
            if (IsInstallingSpark)
            {
                return Text.Get("Performance_SparkInstalling");
            }

            if (IsProfiling)
            {
                return string.Format(Text.Get("Performance_SparkProfiling"), Spark.ProfileSeconds);
            }

            if (!SparkInstalled)
            {
                return server.IsRunning
                    ? Text.Get("Performance_SparkStopToInstall")
                    : string.Format(Text.Get("Performance_SparkAbout"), ModPlatform.For(server.Metadata.Type)?.FolderName);
            }

            return server.IsOnline
                ? string.Format(Text.Get("Performance_SparkReady"), Spark.ProfileSeconds)
                : Text.Get("Performance_SparkStartToProfile");
        }
    }

    /// <summary>The server started, stopped, changed type, or its mods folder changed.</summary>
    internal void OnServerStatusChanged()
    {
        OnPropertyChanged(nameof(ShowSpark));
        OnPropertyChanged(nameof(SparkBuiltIn));
        OnPropertyChanged(nameof(SparkInstalled));
        OnPropertyChanged(nameof(ShowSparkInstall));
        OnPropertyChanged(nameof(ShowSparkProfile));
        OnPropertyChanged(nameof(SparkHint));
        InstallSparkCommand.NotifyCanExecuteChanged();
        ProfileCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanInstallSpark))]
    private async Task InstallSparkAsync()
    {
        SparkError = null;
        IsInstallingSpark = true;
        var meta = server.Metadata;
        try
        {
            await server.Mods.RunFromOtherTabAsync(
                Text.Get("Performance_SparkInstalling"),
                progress => spark.InstallAsync(server.InstanceDir, meta.Type, meta.MinecraftVersion, progress));
        }
        catch (NoCompatibleVersionException)
        {
            SparkError = string.Format(Text.Get("Performance_SparkNoVersion"), meta.Type, meta.MinecraftVersion);
        }
        catch (DownloadVerificationException)
        {
            SparkError = Text.Get("Performance_SparkVerifyFailed");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException
                                       or KeyNotFoundException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            SparkError = string.Format(Text.Get("Performance_SparkInstallFailed"), e.Message);
        }
        finally
        {
            IsInstallingSpark = false;
            OnServerStatusChanged();
        }
    }

    private bool CanInstallSpark() => ShowSparkInstall && server.Mods.CanChange && !IsInstallingSpark;

    /// <summary>Runs spark's profiler, waits for the report link it prints in the console, and opens it.</summary>
    [RelayCommand(CanExecute = nameof(CanProfile))]
    private async Task ProfileAsync()
    {
        if (server.Process is not { IsRunning: true } process)
        {
            return;
        }

        SparkError = null;
        IsProfiling = true;
        var found = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnLine(string line)
        {
            if (Spark.FindReportUrl(line) is { } url)
            {
                found.TrySetResult(url);
            }
        }

        process.OutputReceived += OnLine;
        try
        {
            await server.SendCommandAsync(Spark.ProfileCommand);

            // The upload after the profile usually takes a few seconds; a minute more covers a slow connection.
            var finished = await Task.WhenAny(found.Task, process.Exited, Task.Delay(TimeSpan.FromSeconds(Spark.ProfileSeconds + 60)));
            if (finished == found.Task)
            {
                ReportUrl = found.Task.Result;
                OpenReport();
            }
            else
            {
                SparkError = Text.Get("Performance_SparkNoReport");
            }
        }
        finally
        {
            process.OutputReceived -= OnLine;
            IsProfiling = false;
        }
    }

    private bool CanProfile() => ShowSparkProfile && server.IsOnline && !IsProfiling;

    [RelayCommand]
    private void OpenReport()
    {
        if (ReportUrl is null)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(ReportUrl.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Win32Exception e)
        {
            SparkError = string.Format(Text.Get("Performance_SparkOpenFailed"), e.Message);
        }
    }

    private bool SafeIsSparkInstalled()
    {
        try
        {
            return Spark.IsInstalled(server.InstanceDir, server.Metadata.Type, server.Metadata.MinecraftVersion);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
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
