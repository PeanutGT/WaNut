using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WaNut.App.Resources;
using WaNut.App.Services;
using WaNut.Core.Backups;
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
    private readonly InstanceStore _store;
    private readonly SynchronizationContext _ui;

    public ServerViewModel(
        InstanceMetadata metadata,
        ServerController controller,
        InstanceStore store,
        AppSettingsViewModel app,
        BackupStore backups,
        IDialogService dialogs,
        FirewallService firewall,
        IClipboardService clipboard)
    {
        Metadata = metadata;
        _controller = controller;
        _store = store;
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();
        GameSettings = new GameSettingsViewModel(this);
        PlayerLists = new PlayerListsViewModel(this);
        ServerSettings = new ServerSettingsViewModel(this, app, dialogs);
        Backups = new BackupsViewModel(this, backups, app, dialogs);
        Connect = new ConnectViewModel(this, firewall, clipboard);
        app.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppSettingsViewModel.AdvancedMode))
            {
                ServerSettings.OnAdvancedModeChanged();
            }
        };
        controller.PreparingChanged += (_, _) => StartCommand.NotifyCanExecuteChanged();
    }

    public InstanceMetadata Metadata { get; private set; }

    public string InstanceDir => _store.InstanceDir(Metadata.Id);

    /// <summary>Replaced when the Minecraft version changes, since which settings are game rules depends on it.</summary>
    public GameSettingsViewModel GameSettings { get; private set; }

    public PlayerListsViewModel PlayerLists { get; }

    public ServerSettingsViewModel ServerSettings { get; }

    public BackupsViewModel Backups { get; }

    public ConnectViewModel Connect { get; }

    public string Id => Metadata.Id;

    public string Name => Metadata.Name;

    /// <summary>"build 133" for Paper, "loader 0.19.5" for Fabric, empty for Vanilla.</summary>
    public string LoaderText => Metadata switch
    {
        { LoaderVersion: null } => string.Empty,
        { Type: ServerType.Paper } => $"{Text.Get("Main_LoaderBuild")} {Metadata.LoaderVersion}",
        _ => $"{Text.Get("Main_LoaderVersion")} {Metadata.LoaderVersion}",
    };

    /// <summary>"Vanilla 26.3", "Paper 26.3 build 133".</summary>
    public string TypeAndVersionText => string.Join(' ', new[] { Metadata.Type.ToString(), Metadata.MinecraftVersion, LoaderText }.Where(s => s.Length > 0));

    public ServerProcess? Process { get; private set; }

    public ObservableCollection<string> ConsoleLines { get; } = [];

    public ObservableCollection<string> Players { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning), nameof(IsOnline), nameof(IsBusy), nameof(StatusText))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(StopCommand), nameof(SendCommand))]
    private ServerStatus _status = ServerStatus.Stopped;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy), nameof(StatusText))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private bool _isPreparing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private double? _javaDownloadFraction;

    /// <summary>Set while the world is backed up before a start.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private double? _backupFraction;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _commandText = string.Empty;

    [ObservableProperty]
    private string? _errorText;

    public bool IsRunning => Status != ServerStatus.Stopped;

    public bool IsOnline => Status == ServerStatus.Online;

    /// <summary>Getting ready, starting, saving, or backing up: the moments the peanut wobbles.</summary>
    public bool IsBusy => IsPreparing || Backups.IsWorking || Status is ServerStatus.Starting or ServerStatus.Stopping;

    public string PlayersHeader => string.Format(Text.Get("Server_TabPlayers"), Players.Count);

    public string StatusText => IsPreparing
        ? (BackupFraction, JavaDownloadFraction) switch
        {
            ({ } backup, _) => string.Format(Text.Get("Status_BackingUp"), backup * 100),
            (_, { } java) => string.Format(Text.Get("Status_DownloadingJava"), java * 100),
            _ => Text.Get("Status_Preparing"),
        }
        : Backups.IsWorking ? Backups.WorkText : Text.Get($"Status_{Status}");

    [RelayCommand(CanExecute = nameof(CanStart))]
    private Task StartAsync() => _controller.StartAsync(this);

    private bool CanStart() => Status == ServerStatus.Stopped && !IsPreparing && !Backups.IsWorking && _controller.Preparing is null;

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
        BackupFraction = null;
    }

    internal void ReportBackup(double fraction) => BackupFraction = fraction;

    internal void EndBackup() => BackupFraction = null;

    /// <summary>A backup or restore from the Backups tab started or finished.</summary>
    internal void OnBackupWorkChanged()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(StatusText));
        StartCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsPreparingChanged(bool value) => Backups.OnServerStatusChanged();

    internal void ReportJavaDownload(DownloadProgress progress) => JavaDownloadFraction = progress.Fraction;

    internal void ShowError(string message) => ErrorText = message;

    /// <summary>Saves changed metadata, such as game rules or queued commands, to instance.json.</summary>
    internal void UpdateMetadata(InstanceMetadata metadata)
    {
        _store.Save(metadata);
        Metadata = metadata;
        OnPropertyChanged(nameof(Metadata));
        OnPropertyChanged(nameof(LoaderText));
        OnPropertyChanged(nameof(TypeAndVersionText));
    }

    /// <summary>The server now has another type or Minecraft version: refresh every tab that depends on it.</summary>
    internal void OnTypeOrVersionChanged(InstanceMetadata changed)
    {
        UpdateMetadata(changed);
        GameSettings = new GameSettingsViewModel(this);
        OnPropertyChanged(nameof(GameSettings));
        PlayerLists.Reload();
        Backups.Reload();
    }

    /// <summary>Sends a command WaNut made on the user's behalf, shown in the console like a typed one.</summary>
    internal async Task SendCommandAsync(string command)
    {
        if (Process is not { IsRunning: true } process)
        {
            return;
        }

        AddConsoleLine("> " + command);
        try
        {
            await process.SendCommandAsync(command);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException)
        {
            ErrorText = string.Format(Text.Get("Server_CommandFailed"), e.Message);
        }
    }

    partial void OnStatusChanged(ServerStatus value)
    {
        GameSettings.OnServerStatusChanged();
        PlayerLists.OnServerStatusChanged();
        ServerSettings.OnServerStatusChanged();
        Backups.OnServerStatusChanged();
        if (value == ServerStatus.Online)
        {
            _ = ApplyOnOnlineAsync();
        }
    }

    /// <summary>
    /// Game rules can only be set while the server runs, and whitelist or OP changes made while it was stopped
    /// wait for it too. Both go out as soon as it is online.
    /// </summary>
    private async Task ApplyOnOnlineAsync()
    {
        var pending = Metadata.PendingCommands;
        foreach (var command in Core.Settings.GameSettingsStore.RuleCommands(Metadata).Concat(pending))
        {
            await SendCommandAsync(command);
        }

        if (pending.Count > 0)
        {
            try
            {
                UpdateMetadata(Metadata with { PendingCommands = [] });
            }
            catch (IOException e)
            {
                ErrorText = string.Format(Text.Get("Settings_SaveFailed"), e.Message);
            }

            PlayerLists.RefreshSoon();
        }
    }

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
            t => _ui.Post(_ => OnExited(process, t.Result), null),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.Default);

    }

    private void OnExited(ServerProcess process, int exitCode)
    {
        AddConsoleLine(string.Format(Text.Get("Server_Exited"), exitCode));

        // "stop" ends with exit code 0; anything else the user did not ask for is a crash.
        if (exitCode != 0 && !process.WasKilled)
        {
            ErrorText = string.Format(Text.Get("Server_Crashed"), exitCode);
        }
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
