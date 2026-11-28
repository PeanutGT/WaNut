using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WaNut.App.Resources;
using WaNut.App.Services;
using WaNut.Core.Backups;
using WaNut.Core.Bridge;
using WaNut.Core.Collections;
using WaNut.Core.Crashes;
using WaNut.Core.Downloads;
using WaNut.Core.Hosting;
using WaNut.Core.Instances;
using WaNut.Core.Mods;
using WaNut.Core.Notifications;
using WaNut.Core.Packs;
using WaNut.Core.Profiling;
using WaNut.Core.Remote.Run;
using WaNut.Core.Steps;

namespace WaNut.App.ViewModels;

/// <summary>
/// One server in the list: its details, live status, console and players.
/// </summary>
public sealed partial class ServerViewModel : ObservableObject
{
    /// <summary>Older console lines are dropped so a long-running server does not slow the window down.</summary>
    public const int MaxConsoleLines = 2000;

    private static readonly TimeSpan ConsoleFlushDelay = TimeSpan.FromMilliseconds(100);

    private readonly ProgressEta _javaEta = new();
    private readonly ServerController _controller;
    private readonly InstanceStore _store;
    private readonly DiscordNotifier _discord;
    private readonly SynchronizationContext _ui;

    // Console lines from the server's threads and WaNut's own lines share one queue, so they stay in order, and a
    // burst of output reaches the UI thread as one batch instead of one post per line.
    private readonly ConcurrentQueue<ConsoleLine> _pendingLines = new();
    private int _flushPosted;
    private int _sawWorldGeneration;

    /// <summary>How long a crashed server waits before it is started again.</summary>
    private static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(5);

    private readonly RestartPolicy _restarts = new();
    private readonly EmptyServerTimer _empty = new();

    // Crash restarts only follow a server that got online: one that fails while starting (a bad mod, a port in use)
    // would fail the same way again.
    private bool _wasOnline;

    // Set for an automatic restart, so the crash's last lines stay readable above the new start.
    private bool _keepConsole;

    // This run's output only, for the crash analysis: the console may still show the run before an automatic restart.
    private readonly AppSettingsViewModel _app;
    private readonly Queue<string> _runLines = new();
    private DateTime _startedUtc;

    public ServerViewModel(
        InstanceMetadata metadata,
        ServerController controller,
        InstanceStore store,
        AppSettingsViewModel app,
        BackupStore backups,
        IDialogService dialogs,
        FirewallService firewall,
        PortForwardService portForwards,
        TunnelService tunnels,
        IClipboardService clipboard,
        ModLibrary mods,
        PackLibrary packs,
        Spark spark,
        DiscordNotifier discord,
        PlayerFaces faces,
        BridgeManager bridge)
    {
        _discord = discord;
        _app = app;
        Metadata = metadata;
        _controller = controller;
        _store = store;
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();
        GameSettings = new GameSettingsViewModel(this, _app);
        PlayerLists = new PlayerListsViewModel(this, dialogs, faces);
        ServerSettings = new ServerSettingsViewModel(this, app, dialogs);
        Backups = new BackupsViewModel(this, backups, app, dialogs);
        Connect = new ConnectViewModel(this, firewall, portForwards, tunnels, clipboard, app);
        Mods = new ModsViewModel(this, mods, dialogs, backups);
        Packs = new PacksViewModel(this, packs, dialogs, clipboard, new HotplugViewModel(this, controller, dialogs));
        Performance = new PerformanceViewModel(this, spark);
        Discord = new DiscordViewModel(this, bridge, app, dialogs);
        Files = new ServerFilesViewModel(this, controller, app, dialogs);
        CrashHelp = new CrashHelpViewModel(this, dialogs, clipboard, mods);
        // App settings outlive every server, so a deleted server must not stay reachable through this subscription.
        PropertyChangedEventManager.AddHandler(app, OnAdvancedModeChanged, nameof(AppSettingsViewModel.AdvancedMode));

        // Folding the loading strip is one choice for every server, kept in config.json.
        Steps.IsExpanded = !app.FoldSteps;
        Steps.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StepsViewModel.IsExpanded))
            {
                app.FoldSteps = !Steps.IsExpanded;
            }
        };
        PropertyChangedEventManager.AddHandler(app, OnFoldStepsChanged, nameof(AppSettingsViewModel.FoldSteps));
        ReloadIcon();
    }

    /// <summary>Raised after the server's folder was deleted, so the list can drop it.</summary>
    public event EventHandler? Deleted;

    /// <summary>Raised after the server was copied, so the list can show and select the copy.</summary>
    public event EventHandler<InstanceMetadata>? Duplicated;

    /// <summary>Raised when a fix button wants one of this server's tabs shown.</summary>
    public event EventHandler<ServerTab>? TabRequested;

    public InstanceMetadata Metadata { get; private set; }

    public string InstanceDir => _store.InstanceDir(Metadata.Id);

    /// <summary>Replaced when the Minecraft version changes, since which settings are game rules depends on it.</summary>
    public GameSettingsViewModel GameSettings { get; private set; }

    public PlayerListsViewModel PlayerLists { get; }

    public ServerSettingsViewModel ServerSettings { get; }

    public BackupsViewModel Backups { get; }

    public ConnectViewModel Connect { get; }

    /// <summary>The Files tab, for a server set to run on a VPS.</summary>
    public ServerFilesViewModel Files { get; }

    public PerformanceViewModel Performance { get; }

    public DiscordViewModel Discord { get; }

    public ModsViewModel Mods { get; }

    public PacksViewModel Packs { get; }

    public CrashHelpViewModel CrashHelp { get; }

    /// <summary>The loading strip above the tabs while the server starts, stops or is backed up.</summary>
    public StepsViewModel Steps { get; } = new() { CanFold = true };

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

    public IRunningServer? Process { get; private set; }

    /// <summary>The server's server-icon.png, shown in the list and the header; null when it has none.</summary>
    [ObservableProperty]
    private System.Windows.Media.ImageSource? _icon;

    public bool HasIcon => Icon is not null;

    partial void OnIconChanged(System.Windows.Media.ImageSource? value) => OnPropertyChanged(nameof(HasIcon));

    internal void ReloadIcon() => Icon = ServerIconMaker.Load(InstanceDir);

    public ObservableCollection<ConsoleLine> ConsoleLines { get; } = [];

    public ObservableCollection<string> Players { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning), nameof(IsOnline), nameof(IsBusy), nameof(StatusText), nameof(StatusScene), nameof(ListPlayersText), nameof(ShowsStop))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(StopCommand), nameof(SendCommand))]
    private ServerStatus _status = ServerStatus.Stopped;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy), nameof(StatusText), nameof(StatusScene))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private bool _isPreparing;

    /// <summary>Waiting for another server to get ready first (one prepares at a time). Stop leaves the line.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(ShowsStop))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private bool _isQueued;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private double? _javaDownloadFraction;

    /// <summary>Set while the world is backed up before a start.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private double? _backupFraction;

    /// <summary>What WaNut does on the VPS right now (connecting, Java, uploading, bringing the world back), or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string? _vpsText;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _commandText = string.Empty;

    [ObservableProperty]
    private string? _errorText;

    /// <summary>The latest memory and CPU reading of the running java.exe, shown under the server's name.</summary>
    [ObservableProperty]
    private string? _usageText;

    public bool IsRunning => Status != ServerStatus.Stopped;

    public bool IsOnline => Status == ServerStatus.Online;

    /// <summary>The header shows Stop instead of Start: while running, and while waiting in line to start.</summary>
    public bool ShowsStop => IsRunning || IsQueued;

    /// <summary>Getting ready, starting, saving, or backing up: the moments the peanut wobbles.</summary>
    public bool IsBusy => IsPreparing || Backups.IsWorking || Status is ServerStatus.Starting or ServerStatus.Stopping;

    /// <summary>The peanut next to the status: asleep, starting (also while preparing or backing up), cheering, saving.</summary>
    public string StatusScene => Status switch
    {
        ServerStatus.Online => "online",
        ServerStatus.Stopping => "saving",
        ServerStatus.Starting => "starting",
        _ => IsBusy ? "starting" : "stopped",
    };

    public string PlayersHeader => string.Format(Text.Get("Server_TabPlayers"), Players.Count);

    /// <summary>True when this server is set to run on one of the user's VPS hosts.</summary>
    public bool RunsOnVps => Metadata.VpsId is not null;

    /// <summary>
    /// The newest files are on the VPS (it runs there, or they haven't come back yet): anything changed here would be
    /// overwritten when they come back, so the tabs that change files are locked (plan step 4).
    /// </summary>
    public bool FilesOnVps => Metadata.VpsCopyIsNewer;

    /// <summary>Game settings, mods, backups and the Server tab's file settings can change the files here.</summary>
    public bool CanEditFiles => !FilesOnVps;

    /// <summary>
    /// Stopped with its newest files on the VPS: the settings file can still be edited there (<see cref="CanEditSettings"/>)
    /// without bringing the world back. While it runs, it rewrites that file itself.
    /// </summary>
    public bool ConfigOnVps => FilesOnVps && RunsOnVps && !IsRunning && !IsPreparing;

    /// <summary>The settings file was copied from the VPS and can change; it is sent back after each change.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditSettings))]
    private bool _vpsSettingsReady;

    /// <summary>Game settings and the account check: with the files here, or once the VPS's settings file is here.</summary>
    public bool CanEditSettings => CanEditFiles || (ConfigOnVps && VpsSettingsReady);

    partial void OnVpsSettingsReadyChanged(bool value)
    {
        ServerSettings.OnVpsStateChanged();
        GameSettings.OnVpsStateChanged();
    }

    /// <summary>
    /// A file was saved on the VPS from the Files tab. The settings tabs show a copy of server.properties made earlier,
    /// so it is copied again instead of letting the next settings change send the old one back.
    /// </summary>
    internal void OnVpsFileEdited(string path)
    {
        if (path == Core.Settings.GameSettingsStore.PropertiesFileName && VpsSettingsReady)
        {
            VpsSettingsReady = false;
            if (_settingsShown)
            {
                _ = EnsureVpsSettingsAsync();
            }
        }
    }

    private bool _settingsShown;
    private Task? _pullingSettings;

    /// <summary>
    /// Called when a settings tab is shown (and when the server stops): copies the settings file from the VPS once,
    /// so the tab shows what the VPS really has.
    /// </summary>
    public Task EnsureVpsSettingsAsync()
    {
        _settingsShown = true;
        if (!ConfigOnVps || VpsSettingsReady)
        {
            return Task.CompletedTask;
        }

        return _pullingSettings is { IsCompleted: false } running ? running : _pullingSettings = PullVpsSettingsAsync();
    }

    private async Task PullVpsSettingsAsync()
    {
        if (await _controller.PullSettingsAsync(this) && ConfigOnVps)
        {
            VpsSettingsReady = true;
            GameSettings.Reload();
            ServerSettings.Reload();
        }
    }

    /// <summary>Sends a settings change to the VPS. When that fails the tab goes back to what the VPS has.</summary>
    internal async Task PushVpsSettingsAsync()
    {
        if (!ConfigOnVps)
        {
            return;
        }

        if (!await _controller.PushSettingsAsync(this))
        {
            VpsSettingsReady = false;
            await EnsureVpsSettingsAsync();
        }
    }

    /// <summary>
    /// The Mods tab works straight on the VPS copy while it is the newest: nothing comes back first. Also while the
    /// server runs there, since Linux doesn't lock the jars; a change loads on the next start.
    /// </summary>
    public bool ModsOnVps => FilesOnVps && RunsOnVps && !IsPreparing;

    /// <summary>The Mods tab can change jars: here, or on the VPS (<see cref="ModsOnVps"/>).</summary>
    public bool CanEditMods => CanEditFiles || ModsOnVps;

    /// <summary>Opens this server's folder on its VPS; dispose it to close the connection.</summary>
    internal Task<Core.Storage.IServerFileSystem> OpenVpsFilesAsync(CancellationToken cancellationToken) => _controller.OpenVpsFilesAsync(this, cancellationToken);

    /// <summary>Players can change while a VPS server runs (through its console), not while its files are away.</summary>
    public bool CanEditPlayers => !FilesOnVps || IsRunning;

    public string? VpsBannerText => !FilesOnVps ? null : Text.Get(IsRunning ? "Server_VpsBannerRunning"
        : Metadata.KeepFilesOnVps ? "Server_VpsBannerKept" : "Server_VpsBannerAway");

    /// <summary>"Bring the files back" for a folder kept on the VPS, otherwise "Check the VPS again".</summary>
    public string ReconnectText => Text.Get(Metadata.KeepFilesOnVps ? "Server_VpsBringBack" : "Server_VpsReconnect");

    /// <summary>The banner button: WaNut isn't following the server and isn't busy with it.</summary>
    public bool CanReconnect => FilesOnVps && !IsRunning && !IsPreparing;

    /// <summary>A VPS server whose folder comes back after it stops.</summary>
    private bool BringsFolderBack => RunsOnVps && !Metadata.KeepFilesOnVps;

    [RelayCommand(CanExecute = nameof(CanReconnect))]
    private Task ReconnectAsync() => _controller.ReconnectAsync(this, userAsked: true);

    private void OnVpsStateChanged()
    {
        OnPropertyChanged(nameof(FilesOnVps));
        OnPropertyChanged(nameof(CanEditFiles));
        OnPropertyChanged(nameof(CanEditPlayers));
        OnPropertyChanged(nameof(ModsOnVps));
        OnPropertyChanged(nameof(CanEditMods));
        OnPropertyChanged(nameof(ConfigOnVps));
        OnPropertyChanged(nameof(CanEditSettings));
        OnPropertyChanged(nameof(VpsBannerText));
        OnPropertyChanged(nameof(ReconnectText));
        OnPropertyChanged(nameof(CanReconnect));
        OnPropertyChanged(nameof(RunsOnVps));
        ReconnectCommand.NotifyCanExecuteChanged();
        ServerSettings.OnVpsStateChanged();
        GameSettings.OnVpsStateChanged();
        Files.OnVpsStateChanged();
        Packs.OnVpsStateChanged();
        Mods.OnVpsStateChanged();

        // A run (or a change of where the files are) may have changed the settings file, so copy it again next time.
        if (!ConfigOnVps)
        {
            VpsSettingsReady = false;
        }
        else if (_settingsShown && !VpsSettingsReady)
        {
            _ = EnsureVpsSettingsAsync();
        }
    }

    public string StatusText => IsQueued ? Text.Get("Status_Queued") : VpsText is { } vps && BackupFraction is null ? vps : IsPreparing
        ? (BackupFraction, JavaDownloadFraction) switch
        {
            ({ } backup, _) => string.Format(Text.Get("Status_BackingUp"), backup * 100),
            (_, { } java) => _javaEta.Text is { } eta
                ? $"{string.Format(Text.Get("Status_DownloadingJava"), java * 100)} · {eta}"
                : string.Format(Text.Get("Status_DownloadingJava"), java * 100),
            _ => Text.Get("Status_Preparing"),
        }
        : Backups.IsWorking ? Backups.WorkText : Text.Get($"Status_{Status}");

    [RelayCommand(CanExecute = nameof(CanStart))]
    private Task StartAsync() => _controller.StartAsync(this);

    private bool CanStart() => Status == ServerStatus.Stopped && !IsPreparing && !Backups.IsWorking && !Mods.IsWorking && !Packs.IsWorking;

    /// <summary>Deleting needs the server stopped, its files here, and nothing else working on them.</summary>
    internal bool CanDelete => Status == ServerStatus.Stopped && !IsPreparing && !Backups.IsWorking && !Mods.IsWorking && !Packs.IsWorking && !FilesOnVps;

    /// <summary>The folder is gone: let go of the shared services and tell the list.</summary>
    internal void OnDeleted()
    {
        _controller.Forget(this);
        Deleted?.Invoke(this, EventArgs.Empty);
    }

    internal void OnDuplicated(InstanceMetadata copy) => Duplicated?.Invoke(this, copy);

    private void OnAdvancedModeChanged(object? sender, PropertyChangedEventArgs e) => ServerSettings.OnAdvancedModeChanged();

    private void OnFoldStepsChanged(object? sender, PropertyChangedEventArgs e) => Steps.IsExpanded = !((AppSettingsViewModel)sender!).FoldSteps;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private Task StopAsync() => _controller.StopAsync(this);

    private bool CanStop() => IsQueued || Status is ServerStatus.Starting or ServerStatus.Online;

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
        if (!_keepConsole)
        {
            CrashHelp.Hide();
        }

        JavaDownloadFraction = null;
        DownloadedJava = false;
        _javaEta.Reset();
        IsPreparing = true;
        Steps.Begin(StepLists.ForStart(RunsOnVps, usesVpsCopy: FilesOnVps && Metadata.KeepFilesOnVps));
    }

    /// <summary>
    /// The server's folder comes back from the VPS after a run: nothing may start or change its files meanwhile. Unlike
    /// <see cref="BeginPreparing"/> this keeps the crash card the run may have just shown.
    /// </summary>
    internal void BeginSyncBack(string text)
    {
        VpsText = text;
        IsPreparing = true;
    }

    internal void EndSyncBack()
    {
        VpsText = null;
        IsPreparing = false;
        Steps.Finish(StepLists.Stop);
    }

    internal void EndPreparing()
    {
        // Nothing started (cancelled, failed, or left the line): the start strip goes. A started one keeps it until online.
        if (Process is not { IsRunning: true })
        {
            Steps.Hide(StepLists.Start);
        }

        IsPreparing = false;
        VpsText = null;
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
        OnPropertyChanged(nameof(StatusScene));
        StartCommand.NotifyCanExecuteChanged();
        ServerSettings.OnFilesBusyChanged();
    }

    /// <summary>A mod or plugin install or removal started or finished: the server can't start meanwhile.</summary>
    internal void OnModWorkChanged()
    {
        StartCommand.NotifyCanExecuteChanged();
        ServerSettings.OnFilesBusyChanged();
        Performance.OnServerStatusChanged();
    }

    partial void OnIsPreparingChanged(bool value)
    {
        OnVpsStateChanged();
        Backups.OnServerStatusChanged();
        Mods.OnServerStatusChanged();
        Packs.OnServerStatusChanged();
        ServerSettings.OnFilesBusyChanged();
        Performance.OnServerStatusChanged();
    }

    /// <summary>True once Java was downloaded while this server got ready to start.</summary>
    internal bool DownloadedJava { get; private set; }

    internal void ReportJavaDownload(DownloadProgress progress)
    {
        DownloadedJava = true;
        _javaEta.Report(progress.Fraction, progress.BytesReceived);
        JavaDownloadFraction = progress.Fraction;
    }

    internal void ShowError(string message) => ErrorText = message;

    /// <summary>The port this run listens on, so the next server to start can pick another.</summary>
    internal int RunningPort { get; set; }

    /// <summary>Another server or program had the port: this one moved, and friends need the new address.</summary>
    internal void OnPortMoved(int from, int to)
    {
        AddConsoleLine(string.Format(Text.Get("Server_PortMoved"), from, to));
        ServerSettings.Reload();
    }

    /// <summary>A line from WaNut itself in the console, such as a port opened on the VPS.</summary>
    internal void ShowConsoleNote(string text) => AddConsoleLine(text);

    /// <summary>"3 online" under the name in the server list; empty while the server is not online.</summary>
    public string ListPlayersText => IsOnline ? string.Format(Text.Get("Main_ListPlayers"), Players.Count) : string.Empty;

    /// <summary>The servers running right now, for the Performance tab's total.</summary>
    internal IReadOnlyList<ServerViewModel> RunningServers => _controller.RunningServers;

    internal void RequestTab(ServerTab tab) => TabRequested?.Invoke(this, tab);

    /// <summary>Saves changed metadata, such as game rules or queued commands, to instance.json.</summary>
    internal void UpdateMetadata(InstanceMetadata metadata)
    {
        _store.Save(metadata);
        Metadata = metadata;
        OnPropertyChanged(nameof(Metadata));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(LoaderText));
        OnPropertyChanged(nameof(TypeAndVersionText));
        OnVpsStateChanged();
    }

    /// <summary>The folder may have changed under every tab, e.g. after it came back from the VPS.</summary>
    internal void ReloadFiles()
    {
        GameSettings.Reload();
        PlayerLists.Reload();
        ServerSettings.Reload();
        Backups.Reload();
        Mods.Reload();
        Packs.Reload();
    }

    /// <summary>The server now has another type or Minecraft version: refresh every tab that depends on it.</summary>
    internal void OnTypeOrVersionChanged(InstanceMetadata changed)
    {
        UpdateMetadata(changed);
        GameSettings = new GameSettingsViewModel(this, _app);
        OnPropertyChanged(nameof(GameSettings));
        PlayerLists.Reload();
        Backups.Reload();
        Mods.OnTypeOrVersionChanged();
        Packs.OnTypeOrVersionChanged();
        Performance.OnServerStatusChanged();
    }

    /// <summary>Sends a command WaNut made on the user's behalf, shown in the console like a typed one.</summary>
    /// <summary>False when the command did not reach the server; the error is shown.</summary>
    internal async Task<bool> SendCommandAsync(string command)
    {
        if (Process is not { IsRunning: true } process)
        {
            return false;
        }

        AddConsoleLine("> " + command);
        try
        {
            await process.SendCommandAsync(command);
            return true;
        }
        catch (Exception e) when (e is IOException or InvalidOperationException)
        {
            ErrorText = string.Format(Text.Get("Server_CommandFailed"), e.Message);
            return false;
        }
    }

    partial void OnStatusChanged(ServerStatus value)
    {
        OnVpsStateChanged();
        GameSettings.OnServerStatusChanged();
        PlayerLists.OnServerStatusChanged();
        ServerSettings.OnServerStatusChanged();
        Backups.OnServerStatusChanged();
        Mods.OnServerStatusChanged();
        Packs.OnServerStatusChanged();
        Connect.OnServerStatusChanged(value);
        Performance.OnServerStatusChanged();
        Discord.OnServerStatusChanged();
        UpdateSteps(value);
        if (value == ServerStatus.Online)
        {
            _wasOnline = true;
            Notify(DiscordEvent.Online, "Discord_Online");
            _ = ApplyOnOnlineAsync();
        }
    }

    /// <summary>Moves the loading strip with the server: launching, online, stopping, and the end of a run.</summary>
    private void UpdateSteps(ServerStatus value)
    {
        switch (value)
        {
            case ServerStatus.Starting:
                // A run WaNut follows without getting it ready first, e.g. one still going on the VPS.
                if (Steps.Job != StepLists.Start)
                {
                    Steps.Begin(StepLists.ForStart(RunsOnVps));
                }

                Steps.MoveTo(StepLists.Start, "Launch");
                break;
            case ServerStatus.Online:
                Steps.Finish(StepLists.Start);
                break;
            case ServerStatus.Stopping:
                Steps.Begin(StepLists.ForStop(BringsFolderBack));
                break;
            case ServerStatus.Stopped:
                // Ended while starting: the crash card or error says why.
                Steps.Hide(StepLists.Start);

                // A VPS run's folder comes back next (EndSyncBack finishes the strip), unless WaNut let go of it or it stays there.
                if (Process is RemoteServer { ConnectionLost: false, Detached: false } && !Metadata.KeepFilesOnVps)
                {
                    Steps.MoveTo(StepLists.Stop, "BringBack");
                }
                else
                {
                    Steps.Finish(StepLists.Stop);
                }

                break;
        }
    }

    private void OnPhase(ServerPhase phase)
    {
        switch (phase)
        {
            case ServerPhase.LoadingWorld:
                Steps.MoveTo(StepLists.Start, "LoadWorld");
                break;
            case ServerPhase.SavingWorld:
                Steps.MoveTo(StepLists.Stop, "SaveWorld");
                break;
            case ServerPhase.WorldSaved:
                Steps.MoveTo(StepLists.Stop, "Close");
                break;
        }
    }

    /// <summary>
    /// Game rules can only be set while the server runs, and whitelist or OP changes made while it was stopped
    /// wait for it too. Both go out as soon as it is online.
    /// </summary>
    private async Task ApplyOnOnlineAsync()
    {
        var pending = Metadata.PendingCommands;
        foreach (var command in Core.Settings.GameSettingsStore.RuleCommands(Metadata))
        {
            await SendCommandAsync(command);
        }

        // Game rules are sent again on every start; a whitelist or OP change is only kept here, so one that did not
        // get through stays queued for the next time the server is online.
        var unsent = new List<string>();
        foreach (var command in pending)
        {
            if (!await SendCommandAsync(command))
            {
                unsent.Add(command);
            }
        }

        if (pending.Count > 0)
        {
            try
            {
                UpdateMetadata(Metadata with { PendingCommands = unsent });
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                ErrorText = string.Format(Text.Get("Settings_SaveFailed"), e.Message);
            }

            PlayerLists.RefreshSoon();
        }
    }

    /// <summary>Follows a process that is about to start. Its events arrive on background threads and are moved to the UI thread.</summary>
    internal void Attach(IRunningServer process)
    {
        Process = process;
        Performance.Clear();
        if (!_keepConsole)
        {
            ConsoleLines.Clear();
        }

        _keepConsole = false;
        Players.Clear();
        OnPropertyChanged(nameof(PlayersHeader));

        lock (_runLines)
        {
            _runLines.Clear();
        }

        _startedUtc = DateTime.UtcNow;
        process.OutputReceived += AddConsoleLine;
        process.OutputReceived += KeepRunLine;
        process.StatusChanged += status => _ui.Post(_ => Status = status, null);
        process.OutputReceived += line =>
        {
            if (ServerLogParser.Phase(line) is { } phase)
            {
                _ui.Post(_ => OnPhase(phase), null);
            }
        };
        process.PlayersChanged += () => _ui.Post(_ => SyncPlayers(process), null);
        _wasOnline = false;
        _ = WatchRunningAsync(process);
        process.Exited.ContinueWith(
            t => _ui.Post(_ => _ = OnExitedAsync(process, t.Result), null),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.Default);

    }

    /// <summary>
    /// Every two seconds until the process exits: reads its memory and CPU, backs the world up on the backup
    /// schedule, and stops it once it has been empty for the "stop when nobody is online" time. Runs on the UI thread.
    /// </summary>
    private async Task WatchRunningAsync(IRunningServer process)
    {
        var meter = new CpuMeter(process.ProcessorCount);
        var schedule = new BackupSchedule();
        var idle = new IdleCpuWatch();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        var stopping = false;
        process.OutputReceived += NoteWorldGeneration;
        try
        {
            while (await timer.WaitForNextTickAsync() && Process == process && process.GetUsage() is { } usage)
            {
                var now = DateTimeOffset.UtcNow;
                meter = meter.Cores == process.ProcessorCount ? meter : new CpuMeter(process.ProcessorCount);
                if (meter.Sample(usage.CpuTime, now) is { } cpu)
                {
                    UsageText = string.Format(Text.Get("Server_Usage"), usage.MemoryBytes / (1024.0 * 1024 * 1024), Metadata.MemoryMb / 1024.0, cpu);
                    Performance.Add(new UsageSample(now, cpu, usage.MemoryBytes));
                    if (Interlocked.Exchange(ref _sawWorldGeneration, 0) == 1)
                    {
                        idle.Reset();
                    }
                    else if (idle.Update(Status == ServerStatus.Online, Players.Count, cpu, now))
                    {
                        var minutes = IdleCpuWatch.Duration.TotalMinutes;
                        AddConsoleLine(string.Format(Text.Get("Server_IdleCpuHigh"), minutes, cpu));
                        Performance.IdleWarning = string.Format(Text.Get("Performance_IdleCpuHigh"), minutes);
                    }
                }

                _empty.Update(Status == ServerStatus.Online, Players.Count, now);
                schedule.Update(Status == ServerStatus.Online, Players.Count, now);
                var every = Backups.App.BackupScheduleMinutes;
                // On a VPS the copy here is older than the running world; backups there come in a later step.
                if (!stopping && every > 0 && !Backups.IsWorking && process is not RemoteServer && schedule.IsDue(now, TimeSpan.FromMinutes(every)))
                {
                    schedule.Done(now);
                    AddConsoleLine(Text.Get("Server_ScheduledBackup"));
                    _ = Backups.BackUpWhileRunningAsync(BackupReason.Scheduled);
                }

                if (!stopping && Metadata.AutoStopMinutes > 0 && _empty.IsDue(now, TimeSpan.FromMinutes(Metadata.AutoStopMinutes)))
                {
                    stopping = true;
                    AddConsoleLine(string.Format(Text.Get("Server_AutoStopping"), Metadata.AutoStopMinutes));
                    _ = _controller.StopAsync(this);
                }
            }
        }
        finally
        {
            process.OutputReceived -= NoteWorldGeneration;
            Performance.IdleWarning = null;
            if (Process == process)
            {
                UsageText = null;
            }
        }
    }

    // From the server's output thread: a pregeneration tool is busy on purpose, so the idle CPU check waits.
    private void NoteWorldGeneration(string line)
    {
        if (IdleCpuWatch.IsWorldGeneration(line))
        {
            Interlocked.Exchange(ref _sawWorldGeneration, 1);
        }
    }

    private async Task OnExitedAsync(IRunningServer process, int exitCode)
    {
        if (process is RemoteServer { Detached: true })
        {
            return; // WaNut is closing; the server keeps running on the VPS.
        }

        AddConsoleLine(string.Format(Text.Get("Server_Exited"), exitCode));

        // A crash report written on the VPS only reaches this PC with the folder, so wait for it before looking.
        if (process is RemoteServer && !process.WasKilled && !process.StopRequested)
        {
            await _controller.WaitForSyncBackAsync(this);
        }

        // A stop the user asked for is never a crash. Some failures, such as a port in use, still end with exit code 0,
        // so those runs are checked too; exit code 0 with nothing found is a normal stop, e.g. "/stop" typed in the game.
        var diagnosis = process.WasKilled || process.StopRequested ? null : Diagnose();
        if (process.WasKilled || process.StopRequested || (exitCode == 0 && diagnosis is null))
        {
            Notify(DiscordEvent.Stopped, "Discord_Stopped");
            return;
        }

        CrashHelp.Show(diagnosis, exitCode, RunLines(), CrashAnalyzer.FindCrashReport(InstanceDir, _startedUtc));
        AddConsoleLine(string.Format(Text.Get("Server_CrashSummary"), CrashHelp.Title));
        Notify(DiscordEvent.Crashed, "Discord_Crashed");

        // A mod, port or memory problem fails the same way on every start: restarting would only repeat it.
        if (!Metadata.AutoRestart || !_wasOnline || diagnosis is { WillRepeat: true })
        {
            return;
        }

        if (_restarts.TryRestart(DateTimeOffset.UtcNow))
        {
            Notify(DiscordEvent.Restarting, "Discord_Restarting");
            AddConsoleLine(string.Format(Text.Get("Server_Restarting"), RestartDelay.TotalSeconds, _restarts.Count, RestartPolicy.MaxRestarts));
            _ = RestartAfterCrashAsync(process);
        }
        else
        {
            ErrorText = string.Format(Text.Get("Server_CrashedTooOften"), exitCode, RestartPolicy.MaxRestarts, RestartPolicy.Window.TotalMinutes);
        }
    }

    private void KeepRunLine(string line)
    {
        lock (_runLines)
        {
            _runLines.Enqueue(line);
            if (_runLines.Count > MaxConsoleLines)
            {
                _runLines.Dequeue();
            }
        }
    }

    private List<string> RunLines()
    {
        lock (_runLines)
        {
            return [.. _runLines];
        }
    }

    private CrashDiagnosis? Diagnose()
    {
        string? report = null;
        try
        {
            if (CrashAnalyzer.FindCrashReport(InstanceDir, _startedUtc) is { } path)
            {
                report = File.ReadAllText(path);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The console lines alone still tell most causes.
        }

        return CrashAnalyzer.Analyze(RunLines(), report);
    }

    private async Task RestartAfterCrashAsync(IRunningServer crashed)
    {
        await Task.Delay(RestartDelay);
        await _controller.WaitForSyncBackAsync(this);

        // Only if nothing happened meanwhile: the user may have started this server again or deleted it.
        if (Process == crashed && Status == ServerStatus.Stopped && !IsPreparing && Directory.Exists(InstanceDir))
        {
            _keepConsole = true;
            try
            {
                await _controller.StartAsync(this, automatic: true);
            }
            finally
            {
                _keepConsole = false;
            }
        }
    }

    /// <summary>Queues a console line from any thread; the UI thread adds every waiting line in one go.</summary>
    private void AddConsoleLine(string line)
    {
        // The level is read here, off the UI thread and once per line, not each time a row scrolls into view.
        _pendingLines.Enqueue(new ConsoleLine(line, ServerLogParser.GetLevel(line)));
        if (Interlocked.Exchange(ref _flushPosted, 1) == 0)
        {
            _ = FlushSoonAsync();
        }
    }

    // A modpack starting can print thousands of lines a second. Waiting a moment lets them pile up, so the console
    // updates (and scrolls) a few times a second instead of once per line, and the window stays responsive.
    private async Task FlushSoonAsync()
    {
        await Task.Delay(ConsoleFlushDelay).ConfigureAwait(false);
        _ui.Post(_ => FlushConsoleLines(), null);
    }

    private void FlushConsoleLines()
    {
        Volatile.Write(ref _flushPosted, 0);
        var batch = new List<ConsoleLine>();
        while (_pendingLines.TryDequeue(out var line))
        {
            batch.Add(line);
        }

        // A burst (a modpack starting) can bring more lines than the console keeps: skip the ones that would go at once.
        var skip = Math.Max(0, batch.Count - MaxConsoleLines);
        var excess = ConsoleLines.Count + batch.Count - skip - MaxConsoleLines;
        for (var i = 0; i < excess; i++)
        {
            ConsoleLines.RemoveAt(0);
        }

        for (var i = skip; i < batch.Count; i++)
        {
            ConsoleLines.Add(batch[i]);
        }
    }

    internal void NotifyBackupDone() => Notify(DiscordEvent.BackupDone, "Discord_BackupDone");

    private void Notify(DiscordEvent kind, string key, string? player = null) =>
        _discord.Notify(kind, string.Format(Text.Get(key), DiscordWebhook.Escape(Name), player is null ? null : DiscordWebhook.Escape(player)));

    private void SyncPlayers(IRunningServer process)
    {
        var before = Players.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var after = process.Players;

        // When the server stops, everyone "leaves" at once; that is not news worth a message each.
        if (!process.HasExited)
        {
            foreach (var joined in after.Where(p => !before.Contains(p)))
            {
                Notify(DiscordEvent.PlayerJoined, "Discord_PlayerJoined", joined);
            }

            foreach (var left in before.Where(p => !after.Contains(p, StringComparer.OrdinalIgnoreCase)))
            {
                Notify(DiscordEvent.PlayerLeft, "Discord_PlayerLeft", left);
            }
        }

        // Only joins and leaves change the list, so the Players tab animates just those rows.
        ListSync.Sync(Players, process.Players, StringComparer.OrdinalIgnoreCase);

        OnPropertyChanged(nameof(PlayersHeader));
        OnPropertyChanged(nameof(ListPlayersText));
    }
}
