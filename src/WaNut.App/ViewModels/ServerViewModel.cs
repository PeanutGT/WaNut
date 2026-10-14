using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WaNut.App.Resources;
using WaNut.App.Services;
using WaNut.Core.Downloads;
using WaNut.Core.Hosting;
using WaNut.Core.Instances;

namespace WaNut.App.ViewModels;

/// <summary>
/// One server in the list: its details, live status, console and players.
/// </summary>
public sealed partial class ServerViewModel : ObservableObject
{
    /// <summary>Older console lines are dropped so a long-running server does not slow the window down.</summary>
    public const int MaxConsoleLines = 2000;

    private readonly ServerController _controller;
    private readonly SynchronizationContext _ui;

    public ServerViewModel(InstanceMetadata metadata, ServerController controller)
    {
        Metadata = metadata;
        _controller = controller;
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();
    }

    public InstanceMetadata Metadata { get; }

    public string Id => Metadata.Id;

    public string Name => Metadata.Name;

    /// <summary>"build 133" for Paper, "loader 0.19.5" for Fabric, empty for Vanilla.</summary>
    public string LoaderText => Metadata switch
    {
        { LoaderVersion: null } => string.Empty,
        { Type: ServerType.Paper } => $"{Text.Get("Main_LoaderBuild")} {Metadata.LoaderVersion}",
        _ => $"{Text.Get("Main_LoaderVersion")} {Metadata.LoaderVersion}",
    };

    public ServerProcess? Process { get; private set; }

    public ObservableCollection<string> ConsoleLines { get; } = [];

    public ObservableCollection<string> Players { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning), nameof(IsOnline), nameof(StatusText))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(StopCommand), nameof(SendCommand))]
    private ServerStatus _status = ServerStatus.Stopped;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private bool _isPreparing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private double? _javaDownloadFraction;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _commandText = string.Empty;

    [ObservableProperty]
    private string? _errorText;

    public bool IsRunning => Status != ServerStatus.Stopped;

    public bool IsOnline => Status == ServerStatus.Online;

    public string PlayersHeader => string.Format(Text.Get("Server_TabPlayers"), Players.Count);

    public string StatusText => IsPreparing
        ? JavaDownloadFraction is { } fraction
            ? string.Format(Text.Get("Status_DownloadingJava"), fraction * 100)
            : Text.Get("Status_Preparing")
        : Text.Get($"Status_{Status}");

    [RelayCommand(CanExecute = nameof(CanStart))]
    private Task StartAsync() => _controller.StartAsync(this);

    private bool CanStart() => Status == ServerStatus.Stopped && !IsPreparing;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private Task StopAsync() => _controller.StopAsync(this);

    private bool CanStop() => Status is ServerStatus.Starting or ServerStatus.Online;

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var command = CommandText.Trim().TrimStart('/');
        if (Process is null || command.Length == 0)
        {
            return;
        }

        CommandText = string.Empty;
        AddConsoleLine("> " + command);
        try
        {
            await Process.SendCommandAsync(command);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException)
        {
            ErrorText = string.Format(Text.Get("Server_CommandFailed"), e.Message);
        }
    }

    private bool CanSend() => (Status is ServerStatus.Starting or ServerStatus.Online) && !string.IsNullOrWhiteSpace(CommandText);

    internal void BeginPreparing()
    {
        ErrorText = null;
        JavaDownloadFraction = null;
        IsPreparing = true;
    }

    internal void EndPreparing()
    {
        IsPreparing = false;
        JavaDownloadFraction = null;
    }

    internal void ReportJavaDownload(DownloadProgress progress) => JavaDownloadFraction = progress.Fraction;

    internal void ShowError(string message) => ErrorText = message;

    /// <summary>Follows a process that is about to start. Its events arrive on background threads and are moved to the UI thread.</summary>
    internal void Attach(ServerProcess process)
    {
        Process = process;
        ConsoleLines.Clear();
        Players.Clear();
        OnPropertyChanged(nameof(PlayersHeader));

        process.OutputReceived += line => _ui.Post(_ => AddConsoleLine(line), null);
        process.StatusChanged += status => _ui.Post(_ => Status = status, null);
        process.PlayersChanged += () => _ui.Post(_ => SyncPlayers(process), null);
        process.Exited.ContinueWith(
            t => _ui.Post(_ => AddConsoleLine(string.Format(Text.Get("Server_Exited"), t.Result)), null),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.Default);

    }

    private void AddConsoleLine(string line)
    {
        ConsoleLines.Add(line);
        while (ConsoleLines.Count > MaxConsoleLines)
        {
            ConsoleLines.RemoveAt(0);
        }
    }

    private void SyncPlayers(ServerProcess process)
    {
        Players.Clear();
        foreach (var player in process.Players)
        {
            Players.Add(player);
        }

        OnPropertyChanged(nameof(PlayersHeader));
    }
}
