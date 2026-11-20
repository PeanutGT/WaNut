using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WaNut.App.Resources;
using WaNut.App.Services;
using WaNut.Core.Hosting;
using WaNut.Core.Hotplug;
using WaNut.Core.Hotplug.Preflight;
using WaNut.Core.Hotplug.Readiness;
using WaNut.Core.Hotplug.Safety;

namespace WaNut.App.ViewModels;

/// <summary>
/// The live data pack panel of a server running on a VPS (in the Packs tab): what is in the world's datapacks
/// folder there and whether the server has it enabled, adding, updating and removing packs without a restart, and the
/// watch mode that hot-plugs zips saved into a folder on this PC.
/// </summary>
public sealed partial class HotplugViewModel : ObservableObject
{
    private static readonly string TempFolder = Path.Combine(Path.GetTempPath(), "WaNut", "hotplug");

    private readonly ServerViewModel _server;
    private readonly ServerController _controller;
    private readonly IDialogService _dialogs;
    private readonly SynchronizationContext _ui = SynchronizationContext.Current ?? new SynchronizationContext();
    private readonly Dictionary<(string Name, long Size, DateTime Time), PackMetadata?> _formats = [];

    private HotplugConnection? _connection;
    private DatapackHotplugOrchestrator? _orchestrator;
    private Task<HotplugConnection>? _opening;
    private CancellationTokenSource? _run;
    private CancellationTokenSource? _watch;
    private HotplugWatcher? _watcher;
    private int _refreshes;

    public HotplugViewModel(ServerViewModel server, ServerController controller, IDialogService dialogs)
    {
        _server = server;
        _controller = controller;
        _dialogs = dialogs;
    }

    public ObservableCollection<LiveDatapackViewModel> Packs { get; } = [];

    public ObservableCollection<string> History { get; } = [];

    /// <summary>Shown while the server runs on its VPS; packs then change without a restart.</summary>
    public bool IsAvailable => _server.RunsOnVps && _server.Status == ServerStatus.Online;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy), nameof(ShowProgress))]
    [NotifyCanExecuteChangedFor(nameof(AddCommand), nameof(RemoveCommand), nameof(RefreshCommand), nameof(CancelCommand))]
    private HotplugState _state;

    /// <summary>What is happening now, such as "Debounce 2.1 s…" or "Swapping files…".</summary>
    [ObservableProperty]
    private string? _statusText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressIndeterminate))]
    private double? _fraction;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowResultNotice), nameof(ShowResultError))]
    private string? _resultText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowResultNotice), nameof(ShowResultError))]
    private bool _resultIsError;

    /// <summary>The console's error lines behind the last rollback.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowErrorLog))]
    private string? _errorLog;

    public bool ShowErrorLog => ErrorLog is not null;

    /// <summary>Asks what to do while the server is busy; null when nothing is asked.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoadPrompt))]
    private string? _loadPrompt;

    public bool ShowLoadPrompt => LoadPrompt is not null;

    private TaskCompletionSource<LoadChoice>? _loadAnswer;

    public bool ProgressIndeterminate => Fraction is null;

    public bool ShowResultNotice => ResultText is not null && !ResultIsError;

    public bool ShowResultError => ResultText is not null && ResultIsError;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WatchText))]
    [NotifyCanExecuteChangedFor(nameof(TriggerNowCommand), nameof(AddCommand), nameof(RemoveCommand))]
    private bool _isWatching;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WatchText))]
    private string? _watchFolder;

    public bool IsBusy => State is not (HotplugState.Idle or HotplugState.Completed or HotplugState.RolledBack or HotplugState.Aborted);

    public bool ShowProgress => IsBusy && State != HotplugState.WaitingForData;

    public string WatchText => IsWatching && WatchFolder is not null
        ? string.Format(Text.Get("Hotplug_Watching"), WatchFolder)
        : Text.Get("Hotplug_WatchOff");

    public string Header => string.Format(Text.Get("Hotplug_Header"), Packs.Count);

    /// <summary>The server started, stopped or moved: connect when it runs on the VPS, let go when it doesn't.</summary>
    internal void OnServerStatusChanged()
    {
        OnPropertyChanged(nameof(IsAvailable));
        NotifyCommands();
        if (IsAvailable)
        {
            if (_connection is null && _opening is null)
            {
                _ = RefreshAsync();
            }

            return;
        }

        Close();
    }

    // CanExecute reads IsAvailable, which follows the server status, so the buttons must be re-checked when it moves.
    private void NotifyCommands()
    {
        AddCommand.NotifyCanExecuteChanged();
        RemoveCommand.NotifyCanExecuteChanged();
        RefreshCommand.NotifyCanExecuteChanged();
    }

    internal void OnShown()
    {
        if (IsAvailable && !IsBusy)
        {
            _ = RefreshAsync();
        }
    }

    private bool CanChange() => IsAvailable && !IsBusy && !IsWatching;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        var mine = ++_refreshes;
        IsLoading = true;
        try
        {
            var connection = await ConnectAsync();
            var packs = await LiveDatapacks.ListAsync(connection.Files, connection.Console);
            var audit = await LiveDatapacks.AuditAsync(connection.Files);
            if (mine != _refreshes)
            {
                return;
            }

            Packs.Clear();
            foreach (var pack in packs)
            {
                Packs.Add(new LiveDatapackViewModel(pack, string.Format(Text.Get("Hotplug_RemoveLabel"), pack.FileName)));
            }

            History.Clear();
            foreach (var entry in audit.Take(5))
            {
                History.Add(Describe(entry));
            }

            OnPropertyChanged(nameof(Header));
            await LoadFormatsAsync(connection, mine);
        }
        catch (Exception e) when (Describe(e) is { } message)
        {
            ShowResult(message, error: true);
        }
        finally
        {
            if (mine == _refreshes)
            {
                IsLoading = false;
            }
        }
    }

    private bool CanRefresh() => IsAvailable && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanChange))]
    private async Task AddAsync()
    {
        var picked = _dialogs.PickDatapackZips();
        if (picked.Count > 0)
        {
            await RunAsync(picked, []);
        }
    }

    [RelayCommand(CanExecute = nameof(CanChange))]
    private async Task RemoveAsync(LiveDatapackViewModel? row)
    {
        if (row is null || !row.CanRemove || !_dialogs.Confirm(string.Format(Text.Get("Hotplug_ConfirmRemove"), row.Name)))
        {
            return;
        }

        await RunAsync([], [row.Name]);
    }

    /// <summary>Stops waiting or uploading (and watch mode). A run that reached the swap finishes or undoes it first.</summary>
    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel() => StopWatching();

    [RelayCommand(CanExecute = nameof(IsWatching))]
    private void TriggerNow() => _watcher?.TriggerNow();

    /// <summary>Turns watch mode on (asking for the folder the first time) or off.</summary>
    [RelayCommand]
    private async Task ToggleWatchAsync()
    {
        if (IsWatching)
        {
            StopWatching();
            return;
        }

        if (!IsAvailable || IsBusy)
        {
            return;
        }

        var folder = _dialogs.PickFolder(Text.Get("Hotplug_PickWatchFolder"));
        if (folder is null)
        {
            return;
        }

        WatchFolder = folder;
        HotplugConnection connection;
        try
        {
            connection = await ConnectAsync();
        }
        catch (Exception e) when (Describe(e) is { } message)
        {
            ShowResult(message, error: true);
            return;
        }

        var cts = new CancellationTokenSource();
        _watch = cts;
        _watcher = new HotplugWatcher(Orchestrator(connection), folder, paths => Request(paths, []));
        IsWatching = true;
        try
        {
            await _watcher.RunAsync(report => _ui.Post(_ => Finished(report), null), Progress(), cts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e) when (Describe(e) is { } message)
        {
            ShowResult(message, error: true);
        }
        finally
        {
            if (_watch == cts)
            {
                _watch = null;
                _watcher = null;
                IsWatching = false;
                State = HotplugState.Idle;
                StatusText = null;
            }

            cts.Dispose();
            CloseIfPending();
        }
    }

    private async Task RunAsync(IReadOnlyList<string> add, IReadOnlyList<string> remove)
    {
        var cts = new CancellationTokenSource();
        _run = cts;
        ResultText = null;
        ErrorLog = null;
        try
        {
            var connection = await ConnectAsync(cts.Token);
            var report = await Orchestrator(connection).RunAsync(Request(add, remove), Progress(), cts.Token);
            Finished(report);
        }
        catch (OperationCanceledException)
        {
            State = HotplugState.Aborted;
            ShowResult(Text.Get("Hotplug_Cancelled"), error: false);
        }
        catch (Exception e) when (Describe(e) is { } message)
        {
            State = HotplugState.Aborted;
            ShowResult(message, error: true);
        }
        finally
        {
            _run = null;
            cts.Dispose();
            StatusText = null;
            Fraction = null;
            CloseIfPending();
        }
    }

    private HotplugRequest Request(IReadOnlyList<string> add, IReadOnlyList<string> remove) =>
        new(add, remove, _server.Metadata.MinecraftVersion, ConfirmAsync);

    /// <summary>Asks on the UI thread, wherever the orchestrator is.</summary>
    private Task<bool> ConfirmAsync(IReadOnlyList<DatapackInspection> warnings, CancellationToken cancellationToken)
    {
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ui.Post(_ => answer.TrySetResult(_dialogs.Confirm(Warning(warnings))), null);
        return answer.Task.WaitAsync(cancellationToken);
    }

    private static string Warning(IReadOnlyList<DatapackInspection> warnings)
    {
        var lines = warnings.Select(i =>
        {
            var parts = new List<string>();
            if (i.Format == FormatCheck.Mismatch)
            {
                parts.Add(string.Format(Text.Get("Hotplug_WarnFormat"), FormatRange(i.Metadata), i.ExpectedFormat));
            }

            if (i.Risk == PackRisk.AppliesAfterRestart)
            {
                parts.Add(string.Format(Text.Get("Hotplug_WarnRestart"), string.Join(", ", i.RiskyFolders)));
            }

            return $"• {i.FileName}: {string.Join(" ", parts)}";
        });
        return Text.Get("Hotplug_WarnIntro") + "\n\n" + string.Join("\n", lines);
    }

    private void Finished(HotplugReport report)
    {
        State = report.Outcome;
        ErrorLog = report.ConsoleLines is { Count: > 0 } lines ? string.Join(Environment.NewLine, lines) : null;
        var names = string.Join(", ", report.Items.Select(i => i.FileName));
        switch (report.Outcome)
        {
            case HotplugState.Completed:
                ShowResult(string.Format(Text.Get("Hotplug_Done"), names) + (report.Message is { } warn ? " " + warn : string.Empty), error: false);
                break;
            case HotplugState.RolledBack:
                ShowResult(string.Format(Text.Get("Hotplug_RolledBack"), report.Message), error: true);
                break;
            default:
                ShowResult(report.Holder is { } holder
                    ? string.Format(Text.Get("Hotplug_Locked"), holder.Machine ?? "?", holder.Since?.ToLocalTime().ToString("t", CultureInfo.CurrentCulture) ?? "?")
                    : string.Format(Text.Get("Hotplug_Aborted"), report.Message), error: true);
                break;
        }

        if (IsAvailable)
        {
            _ = RefreshAsync();
        }
    }

    private IProgress<HotplugProgress> Progress() => new SyncProgress(p => _ui.Post(_ =>
    {
        State = p.State;
        Fraction = p.Fraction;
        StatusText = p.State switch
        {
            HotplugState.WaitingForData => Waiting(p.Readiness),
            HotplugState.PreFlightCheck => Text.Get("Hotplug_StateChecking"),
            HotplugState.UploadingStaging => string.Format(Text.Get("Hotplug_StateUploading"), p.Detail, (p.Fraction ?? 0).ToString("P0", CultureInfo.CurrentCulture)),
            HotplugState.VerifyingRemote => string.Format(Text.Get("Hotplug_StateVerifying"), p.Detail),
            HotplugState.AtomicSwap => Text.Get("Hotplug_StateSwapping"),
            HotplugState.ApplyingAndReloading => Text.Get("Hotplug_StateReloading"),
            _ => null,
        };
    }, null));

    private static string Waiting(ReadinessStatus? status) => status switch
    {
        { Phase: ReadinessPhase.Debouncing, DebounceRemaining: { } left } =>
            string.Format(Text.Get("Hotplug_StateDebounce"), left.TotalSeconds.ToString("0.0", CultureInfo.CurrentCulture)),
        { Phase: ReadinessPhase.Settling, Detail: { } name } => string.Format(Text.Get("Hotplug_StateSettling"), name),
        { Phase: ReadinessPhase.WaitingForData, Detail: { } name } when name != "files" => string.Format(Text.Get("Hotplug_StateWaitingFor"), name),
        _ => Text.Get("Hotplug_StateWaiting"),
    };

    private async Task<HotplugConnection> ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_connection is { } open)
        {
            return open;
        }

        _opening ??= _controller.OpenVpsHotplugAsync(_server, CancellationToken.None);
        try
        {
            var connection = await _opening.WaitAsync(cancellationToken);
            if (!IsAvailable)
            {
                await connection.Files.DisposeAsync();
                throw new InvalidOperationException(Text.Get("Hotplug_NotRunning"));
            }

            _connection = connection;
            return connection;
        }
        finally
        {
            if (_opening?.IsCompleted == true)
            {
                _opening = null;
            }
        }
    }

    private DatapackHotplugOrchestrator Orchestrator(HotplugConnection connection)
    {
        if (_orchestrator is null || !ReferenceEquals(_lastConnection, connection))
        {
            var gate = new LoadReloadGate(AskLoadAsync, Queued);
            _orchestrator = new DatapackHotplugOrchestrator(connection, new HotplugHooks(gate, new ReloadLogSentinel()));
            _lastConnection = connection;
        }

        return _orchestrator;
    }

    private HotplugConnection? _lastConnection;

    /// <summary>Shows the busy-server question in the panel and waits for a button, wherever the orchestrator is.</summary>
    private Task<LoadChoice> AskLoadAsync(ServerLoad load, CancellationToken cancellationToken)
    {
        var answer = new TaskCompletionSource<LoadChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ui.Post(_ =>
        {
            _loadAnswer?.TrySetResult(LoadChoice.Cancel);
            _loadAnswer = answer;
            LoadPrompt = string.Format(
                Text.Get("Hotplug_BusyPrompt"),
                load.Mspt?.ToString("0.#", CultureInfo.CurrentCulture) ?? "?",
                load.Players?.ToString(CultureInfo.CurrentCulture) ?? "?",
                LoadReloadGate.DefaultQueueBelowPlayers);
        }, null);
        var registration = cancellationToken.Register(() => _ui.Post(_ => Answer(answer, LoadChoice.Cancel), null));
        _ = answer.Task.ContinueWith(_ => registration.Dispose(), TaskScheduler.Default);
        return answer.Task.WaitAsync(cancellationToken);
    }

    private void Queued(ServerLoad load) => _ui.Post(_ =>
        StatusText = string.Format(
            Text.Get("Hotplug_StateQueued"),
            load.Players?.ToString(CultureInfo.CurrentCulture) ?? "?",
            LoadReloadGate.DefaultQueueBelowPlayers), null);

    [RelayCommand]
    private void RunAnyway() => Answer(_loadAnswer, LoadChoice.RunNow);

    [RelayCommand]
    private void QueueUntilQuiet() => Answer(_loadAnswer, LoadChoice.Queue);

    [RelayCommand]
    private void DontHotplug() => Answer(_loadAnswer, LoadChoice.Cancel);

    private void Answer(TaskCompletionSource<LoadChoice>? answer, LoadChoice choice)
    {
        if (answer is null)
        {
            return;
        }

        answer.TrySetResult(choice);
        if (ReferenceEquals(answer, _loadAnswer))
        {
            _loadAnswer = null;
            LoadPrompt = null;
        }
    }

    /// <summary>Reads pack_format from each zip once (by name, size and time); datapacks are small.</summary>
    private async Task LoadFormatsAsync(HotplugConnection connection, int refresh)
    {
        var folder = await LiveDatapacks.FolderAsync(connection.Files);
        foreach (var row in Packs.ToList())
        {
            var key = (row.Pack.FileName, row.Pack.SizeBytes, row.Pack.ModifiedUtc);
            if (!_formats.TryGetValue(key, out var metadata))
            {
                metadata = await LiveDatapacks.ReadMetadataAsync(connection.Files, folder, row.Pack, TempFolder);
                _formats[key] = metadata;
            }

            if (refresh != _refreshes)
            {
                return;
            }

            row.FormatText = metadata is null ? Text.Get("Hotplug_FormatUnknown") : string.Format(Text.Get("Hotplug_Format"), FormatRange(metadata));
        }
    }

    private static string FormatRange(PackMetadata? metadata) => metadata switch
    {
        { MinFormat: { } min, MaxFormat: { } max } when min != max => $"{min}–{max}",
        { MinFormat: { } min } => min.ToString(CultureInfo.InvariantCulture),
        { MaxFormat: { } max } => max.ToString(CultureInfo.InvariantCulture),
        _ => "?",
    };

    private string Describe(HotplugAuditEntry entry)
    {
        var when = entry.Time.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        var outcome = Text.Get(entry.Outcome switch
        {
            HotplugState.Completed => "Hotplug_HistoryDone",
            HotplugState.RolledBack => "Hotplug_HistoryRolledBack",
            _ => "Hotplug_HistoryAborted",
        });
        var packs = entry.Packs.Count == 0 ? string.Empty : " · " + string.Join(", ", entry.Packs.Select(p => p.Name));
        return $"{when} · {entry.Machine} · {outcome}{packs}";
    }

    private string? Describe(Exception e) => e switch
    {
        HotplugWorldMissingException => e.Message,
        _ => _controller.DescribeVpsFilesProblem(e),
    };

    private void ShowResult(string message, bool error)
    {
        ResultText = message;
        ResultIsError = error;
    }

    private void StopWatching()
    {
        _watch?.Cancel();
        _run?.Cancel();
    }

    /// <summary>Lets go of the VPS when the server stops there; a hot-plug in its swap still finishes first.</summary>
    internal void Close()
    {
        StopWatching();
        _refreshes++;
        IsLoading = false;
        var connection = _connection;
        var busy = _orchestrator?.IsBusy == true;
        _connection = null;
        _orchestrator = null;
        _lastConnection = null;
        Packs.Clear();
        History.Clear();
        OnPropertyChanged(nameof(Header));
        if (connection is null)
        {
            return;
        }

        if (busy)
        {
            // Closing the SSH connection now could stop the swap script half way; let the run end first.
            _closeAfterRun = connection;
            return;
        }

        Dispose(connection);
    }

    private HotplugConnection? _closeAfterRun;

    private void CloseIfPending()
    {
        if (_closeAfterRun is { } connection && !ReferenceEquals(connection, _connection))
        {
            _closeAfterRun = null;
            Dispose(connection);
        }
    }

    private static void Dispose(HotplugConnection connection) =>
        _ = connection.Files.DisposeAsync().AsTask().ContinueWith(_ => { }, TaskScheduler.Default);

    partial void OnStateChanged(HotplugState value) => TriggerNowCommand.NotifyCanExecuteChanged();

    private sealed class SyncProgress(Action<HotplugProgress> report) : IProgress<HotplugProgress>
    {
        public void Report(HotplugProgress value) => report(value);
    }
}
