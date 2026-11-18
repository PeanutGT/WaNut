using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WaNut.App.Resources;
using WaNut.App.Services;
using WaNut.Core.Collections;
using WaNut.Core.Storage;
using WaNut.Core.Workspace;

namespace WaNut.App.ViewModels;

/// <summary>
/// The Files tab of a server that runs on a VPS: its folder as a tree that opens one folder at a time, how much
/// space each kind of file takes, and a text editor for small settings files. Reading works any time; editing only
/// while the server is stopped and its newest files are on the VPS, like the settings tabs. The clean-up panel
/// lists old logs, disabled mods, old backups and loose world copies (a dry run), then removes only what stays checked.
/// </summary>
public sealed partial class ServerFilesViewModel : ObservableObject
{
    private readonly ServerViewModel _server;
    private readonly ServerController _controller;
    private readonly AppSettingsViewModel _app;
    private readonly IDialogService _dialogs;
    private readonly SynchronizationContext _ui = SynchronizationContext.Current ?? new SynchronizationContext();

    private WorkspaceBrowser? _browser;
    private CancellationTokenSource? _cts;
    private TextDocument? _document;
    private bool _connecting;
    private bool _shown;
    private bool _syncingRows;
    private bool _undoingSelection;
    private (bool Running, bool Preparing, bool FilesOnVps)? _lastState;

    public ServerFilesViewModel(ServerViewModel server, ServerController controller, AppSettingsViewModel app, IDialogService dialogs)
    {
        _server = server;
        _controller = controller;
        _app = app;
        _dialogs = dialogs;
    }

    public ObservableCollection<FileRowViewModel> Rows { get; } = [];

    public ObservableCollection<UsageRowViewModel> Usage { get; } = [];

    public ObservableCollection<CleanupItemViewModel> CleanupItems { get; } = [];

    /// <summary>The tab only exists for a server set to run on a VPS.</summary>
    public bool IsAvailable => _server.RunsOnVps;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowTree), nameof(ShowEmpty))]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorText;

    [ObservableProperty]
    private string? _statusText;

    [ObservableProperty]
    private string _totalText = string.Empty;

    [ObservableProperty]
    private bool _isMeasuring;

    [ObservableProperty]
    private FileRowViewModel? _selected;

    /// <summary>Why the selected entry can't be edited, or what it is when it's a folder; null while the editor is shown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowInfo), nameof(ShowPlaceholder))]
    private string? _infoText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty), nameof(ShowEditor), nameof(ShowPlaceholder))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(RevertCommand))]
    private string _editorText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEditor), nameof(ShowPlaceholder), nameof(EditorTitle), nameof(IsDirty))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(RevertCommand))]
    private bool _hasDocument;

    public bool ShowTree => !IsLoading || Rows.Count > 0;

    public bool ShowEmpty => !IsLoading && Rows.Count == 0 && ErrorText is null;

    public bool ShowEditor => HasDocument && !IsCleanupOpen;

    public bool ShowInfo => InfoText is not null && !HasDocument && !IsCleanupOpen;

    public bool ShowPlaceholder => !HasDocument && InfoText is null && !IsCleanupOpen;

    /// <summary>The clean-up panel takes the editor's place; an unsaved edit stays and comes back when it closes.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEditor), nameof(ShowInfo), nameof(ShowPlaceholder))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isCleanupOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCleanupList), nameof(ShowCleanupEmpty))]
    [NotifyCanExecuteChangedFor(nameof(OpenCleanupCommand), nameof(RunCleanupCommand))]
    private bool _isCleaning;

    [ObservableProperty]
    private string? _cleanupStatus;

    [ObservableProperty]
    private string _cleanupSummary = string.Empty;

    [ObservableProperty]
    private bool _cleanupHasRisk;

    public bool ShowCleanupList => CleanupItems.Count > 0;

    public bool ShowCleanupEmpty => !IsCleaning && CleanupItems.Count == 0;

    /// <summary>Deleting needs the same as editing: the server stopped and its newest files on the VPS.</summary>
    public bool ShowCleanupLocked => !CanEditNow;

    public string EditorTitle => _document?.Path ?? string.Empty;

    /// <summary>The text differs from what the VPS has (ignoring line endings, which the editor shows its own way).</summary>
    public bool IsDirty => _document is not null && Unify(EditorText) != Unify(_document.Text);

    public bool CanEditNow => _server.FilesOnVps && !_server.IsRunning && !_server.IsPreparing;

    /// <summary>Server logs are rewritten by the server all the time, so a saved edit would be lost.</summary>
    public bool EditorReadOnly => !CanEditNow || (Selected is { } row && TextFilePolicy.ServerOwned(row.Node));

    /// <summary>Says why the editor is read-only; null when editing is allowed.</summary>
    public string? ReadOnlyNote =>
        _server.IsRunning ? Text.Get("Files_ReadOnlyRunning")
        : _server.IsPreparing ? Text.Get("Files_ReadOnlyBusy")
        : !_server.FilesOnVps ? Text.Get("Files_ReadOnlyOnPc")
        : Selected is { } row && TextFilePolicy.ServerOwned(row.Node) ? Text.Get("Files_ReadOnlyLog")
        : null;

    public bool ShowReadOnlyNote => ReadOnlyNote is not null;

    /// <summary>The tab was shown: connects to the VPS the first time.</summary>
    public void OnShown()
    {
        _shown = true;
        if (_browser is null && IsAvailable)
        {
            _ = ConnectAsync();
        }
    }

    public void OnHidden() => _shown = false;

    /// <summary>Where the server runs or whether its files are away changed.</summary>
    internal void OnVpsStateChanged()
    {
        OnPropertyChanged(nameof(IsAvailable));
        OnPropertyChanged(nameof(CanEditNow));
        OnPropertyChanged(nameof(EditorReadOnly));
        OnPropertyChanged(nameof(ReadOnlyNote));
        OnPropertyChanged(nameof(ShowReadOnlyNote));
        OnPropertyChanged(nameof(ShowCleanupLocked));
        SaveCommand.NotifyCanExecuteChanged();
        RunCleanupCommand.NotifyCanExecuteChanged();

        if (!IsAvailable)
        {
            Close();
            if (_shown)
            {
                _server.RequestTab(ServerTab.Console);
            }

            return;
        }

        // A run or a sync changes the files, so what is listed is out of date once the state moves.
        var state = (_server.IsRunning, _server.IsPreparing, _server.FilesOnVps);
        var changed = _lastState is { } last && last != state;
        _lastState = state;
        if (changed && _browser is not null && !_connecting)
        {
            _ = RefreshAsync();
        }
    }

    /// <summary>Lets go of the VPS connection, e.g. when the server stops running there.</summary>
    internal void Close()
    {
        _cts?.Cancel();
        _cts = null;
        var browser = _browser;
        _browser = null;
        if (browser is not null)
        {
            _ = browser.FileSystem.DisposeAsync().AsTask().ContinueWith(_ => { }, TaskScheduler.Default);
        }

        _document = null;
        _syncingRows = true;
        Rows.Clear();
        Usage.Clear();
        Selected = null;
        _syncingRows = false;
        EditorText = string.Empty;
        HasDocument = false;
        InfoText = null;
        TotalText = string.Empty;
        ErrorText = null;
        StatusText = null;
        CleanupItems.Clear();
        IsCleanupOpen = false;
        CleanupStatus = null;
        NotifyListChanged();
        NotifyCleanupChanged();
    }

    [RelayCommand]
    private Task RefreshAsync() => _browser is null ? ConnectAsync() : ReloadAsync();

    private async Task ConnectAsync()
    {
        if (_connecting)
        {
            return;
        }

        _connecting = true;
        IsLoading = true;
        ErrorText = null;
        var cts = _cts = new CancellationTokenSource();
        try
        {
            var fileSystem = await _controller.OpenVpsFilesAsync(_server, cts.Token);
            var browser = new WorkspaceBrowser(fileSystem);
            try
            {
                await browser.Tree.LoadRootAsync(cts.Token);
            }
            catch
            {
                await fileSystem.DisposeAsync();
                throw;
            }

            if (cts.IsCancellationRequested)
            {
                await fileSystem.DisposeAsync();
                return;
            }

            _browser = browser;
            OpenCleanupCommand.NotifyCanExecuteChanged();
            SyncRows();
            _ = MeasureAsync(browser, cts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e) when (_controller.DescribeVpsFilesProblem(e) is { } text)
        {
            ErrorText = string.Format(Text.Get("Files_ConnectFailed"), text);
        }
        finally
        {
            _connecting = false;
            IsLoading = false;
            NotifyListChanged();
        }
    }

    private async Task ReloadAsync()
    {
        if (_browser is not { } browser || _connecting)
        {
            return;
        }

        _connecting = true;
        IsLoading = true;
        ErrorText = null;
        var token = _cts?.Token ?? CancellationToken.None;
        try
        {
            await browser.Tree.RefreshAsync(token);
            SyncRows();
            _ = MeasureAsync(browser, token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e) when (_controller.DescribeVpsFilesProblem(e) is { } text)
        {
            ErrorText = string.Format(Text.Get("Files_ConnectFailed"), text);
        }
        finally
        {
            _connecting = false;
            IsLoading = false;
            NotifyListChanged();
        }
    }

    private async Task MeasureAsync(WorkspaceBrowser browser, CancellationToken token)
    {
        IsMeasuring = true;
        try
        {
            var usage = await browser.MeasureAsync(token);
            if (!ReferenceEquals(browser, _browser))
            {
                return;
            }

            ListSync.Sync(
                Usage,
                usage.Categories,
                c => c.Category,
                u => u.Usage.Category,
                c => new UsageRowViewModel(c, usage.Fraction(c)),
                isCurrent: (u, c) => u.Usage == c && Math.Abs(u.Fraction - usage.Fraction(c)) < 1e-9);
            TotalText = string.Format(Text.Get("Files_SpaceTotal"), ByteSize.Format(usage.TotalBytes));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e) when (_controller.DescribeVpsFilesProblem(e) is { })
        {
            // The tree still works without the size chart, so a failed count is not worth an error.
            TotalText = string.Empty;
        }
        finally
        {
            IsMeasuring = false;
        }
    }

    public bool SelectedIsDirectory => Selected is { IsDirectory: true };

    [RelayCommand]
    private Task ToggleSelectedAsync() => ToggleAsync(Selected);

    [RelayCommand]
    private async Task ToggleAsync(FileRowViewModel? row)
    {
        if (row is not { IsDirectory: true } || _browser is not { } browser)
        {
            return;
        }

        try
        {
            await browser.Tree.ToggleAsync(row.Path, _cts?.Token ?? CancellationToken.None);
            SyncRows();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e) when (_controller.DescribeVpsFilesProblem(e) is { } text)
        {
            ErrorText = string.Format(Text.Get("Files_ConnectFailed"), text);
        }
    }

    /// <summary>Makes the list match the tree; the selected line stays selected even though its row object is replaced.</summary>
    private void SyncRows()
    {
        if (_browser is null)
        {
            return;
        }

        var selectedPath = Selected?.Path;
        _syncingRows = true;
        try
        {
            ListSync.Sync(Rows, _browser.Tree.Rows, r => r.Node.Path, vm => vm.Path, r => new FileRowViewModel(r), isCurrent: (vm, r) => vm.Row == r);
            Selected = selectedPath is null ? null : Rows.FirstOrDefault(r => r.Path == selectedPath);
        }
        finally
        {
            _syncingRows = false;
        }

        NotifyListChanged();
    }

    private void NotifyListChanged()
    {
        OnPropertyChanged(nameof(ShowTree));
        OnPropertyChanged(nameof(ShowEmpty));
    }

    partial void OnSelectedChanged(FileRowViewModel? oldValue, FileRowViewModel? newValue)
    {
        OnPropertyChanged(nameof(SelectedIsDirectory));
        OnPropertyChanged(nameof(EditorReadOnly));
        OnPropertyChanged(nameof(ReadOnlyNote));
        OnPropertyChanged(nameof(ShowReadOnlyNote));
        if (_syncingRows || _undoingSelection)
        {
            return;
        }

        if (IsDirty && !_dialogs.Confirm(string.Format(Text.Get("Files_DiscardAsk"), _document!.Path)))
        {
            // The list is still updating from this click; put the old line back once it is done.
            _undoingSelection = true;
            _ui.Post(_ =>
            {
                Selected = oldValue is not null && Rows.Contains(oldValue) ? oldValue : Rows.FirstOrDefault(r => r.Path == _document?.Path);
                _undoingSelection = false;
            }, null);
            return;
        }

        _ = ShowAsync(newValue);
    }

    private async Task ShowAsync(FileRowViewModel? row)
    {
        _document = null;
        HasDocument = false;
        EditorText = string.Empty;
        ErrorText = null;
        StatusText = null;
        InfoText = null;
        if (row is null || _browser is not { } browser)
        {
            return;
        }

        var node = row.Node;
        var problem = TextFilePolicy.Check(node);
        if (problem is { } p)
        {
            InfoText = DescribeEntry(node, p);
            return;
        }

        try
        {
            var document = await browser.OpenTextAsync(node, _cts?.Token ?? CancellationToken.None);
            if (Selected?.Path != node.Path)
            {
                return;
            }

            _document = document;
            EditorText = Unify(document.Text);
            HasDocument = true;
            OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(EditorTitle));
        }
        catch (TextEditException e)
        {
            InfoText = DescribeEntry(node, e.Problem);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e) when (_controller.DescribeVpsFilesProblem(e) is { } text)
        {
            ErrorText = string.Format(Text.Get("Files_ReadFailed"), text);
        }
    }

    private static string DescribeEntry(ServerFileNode node, TextEditProblem problem)
    {
        var meta = node.IsDirectory
            ? Text.Get("Files_FolderKind")
            : $"{ByteSize.Format(node.SizeBytes)} · {node.LastModifiedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)}";
        var why = problem switch
        {
            TextEditProblem.Folder => Text.Get("Files_InfoFolder"),
            TextEditProblem.TooLarge => Text.Get("Files_InfoTooLarge"),
            _ => Text.Get("Files_InfoNotText"),
        };
        return $"{node.Name}{Environment.NewLine}{meta}{Environment.NewLine}{Environment.NewLine}{why}";
    }

    private bool CanSave() => IsDirty && CanEditNow && !EditorReadOnly && !IsCleanupOpen;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (_browser is not { } browser || _document is not { } document)
        {
            return;
        }

        ErrorText = null;
        StatusText = null;
        var token = _cts?.Token ?? CancellationToken.None;
        try
        {
            var result = await browser.SaveTextAsync(document, EditorText, cancellationToken: token);
            if (result.Outcome == TextSaveOutcome.ChangedMeanwhile)
            {
                if (!_dialogs.Confirm(Text.Get("Files_ChangedAsk")))
                {
                    return;
                }

                result = await browser.SaveTextAsync(document, EditorText, overwriteChanges: true, cancellationToken: token);
            }

            if (result.Saved is { } saved)
            {
                _document = saved;
                OnPropertyChanged(nameof(IsDirty));
                SaveCommand.NotifyCanExecuteChanged();
                RevertCommand.NotifyCanExecuteChanged();
                StatusText = Text.Get("Files_Saved");
                _server.OnVpsFileEdited(saved.Path);
                _ = RefreshQuietlyAsync(browser, token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e) when (_controller.DescribeVpsFilesProblem(e) is { } text)
        {
            ErrorText = string.Format(Text.Get("Files_SaveFailed"), text);
        }
    }

    // The saved file's size and time changed; the tree and the size chart should show it without a button press.
    private async Task RefreshQuietlyAsync(WorkspaceBrowser browser, CancellationToken token)
    {
        try
        {
            await browser.Tree.RefreshAsync(token);
            if (ReferenceEquals(browser, _browser))
            {
                SyncRows();
                await MeasureAsync(browser, token);
            }
        }
        catch (Exception e) when (e is OperationCanceledException || _controller.DescribeVpsFilesProblem(e) is not null)
        {
        }
    }

    private bool CanRevert() => IsDirty;

    [RelayCommand(CanExecute = nameof(CanRevert))]
    private void Revert()
    {
        if (_document is { } document)
        {
            EditorText = Unify(document.Text);
            StatusText = null;
        }
    }

    partial void OnEditorTextChanged(string value) => StatusText = null;

    private static string Unify(string text) => LineEndings.Apply(text, crlf: false);

    private bool CanOpenCleanup() => _browser is not null && !IsCleaning;

    /// <summary>The dry run: lists what could go without changing anything, so it works while the server runs too.</summary>
    [RelayCommand(CanExecute = nameof(CanOpenCleanup))]
    private async Task OpenCleanupAsync()
    {
        IsCleanupOpen = true;
        CleanupStatus = null;
        await EvaluateCleanupAsync();
    }

    [RelayCommand]
    private void CloseCleanup()
    {
        IsCleanupOpen = false;
        CleanupStatus = null;
    }

    private async Task EvaluateCleanupAsync()
    {
        if (_browser is not { } browser)
        {
            return;
        }

        IsCleaning = true;
        ErrorText = null;
        try
        {
            var plan = await Organizer(browser).EvaluateAsync(CleanupRulesNow(), _cts?.Token ?? CancellationToken.None);
            if (!ReferenceEquals(browser, _browser))
            {
                return;
            }

            CleanupItems.Clear();
            foreach (var item in plan.Items)
            {
                CleanupItems.Add(new CleanupItemViewModel(item, NotifyCleanupChanged));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e) when (_controller.DescribeVpsFilesProblem(e) is { } text)
        {
            ErrorText = string.Format(Text.Get("Files_CleanupFailed"), text);
        }
        finally
        {
            IsCleaning = false;
            NotifyCleanupChanged();
        }
    }

    private bool CanRunCleanup() => CanEditNow && !IsCleaning && CleanupItems.Any(i => i.IsChecked);

    [RelayCommand(CanExecute = nameof(CanRunCleanup))]
    private async Task RunCleanupAsync()
    {
        if (_browser is not { } browser)
        {
            return;
        }

        var selected = CleanupItems.Where(i => i.IsChecked).Select(i => i.Item).ToList();
        var ask = selected.Any(i => i.Risky) ? "Files_CleanupConfirmRisky" : "Files_CleanupConfirm";
        if (!_dialogs.Confirm(string.Format(Text.Get(ask), selected.Count, ByteSize.Format(CleanupPlan.FreedBytes(selected)))))
        {
            return;
        }

        IsCleaning = true;
        ErrorText = null;
        CleanupStatus = null;
        var token = _cts?.Token ?? CancellationToken.None;
        string? status = null;
        try
        {
            // Checked again here: the server may have started while the dialog was open.
            if (!CanEditNow)
            {
                return;
            }

            var result = await Organizer(browser).ExecutePlanAsync(selected, token);
            status = string.Format(Text.Get("Files_CleanupDone"), result.Deleted, result.Archived, ByteSize.Format(result.FreedBytes));
            if (result.Skipped.Count > 0)
            {
                status += " " + string.Format(Text.Get("Files_CleanupSkipped"), result.Skipped.Count);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (CleanupException e)
        {
            ErrorText = string.Format(Text.Get("Files_CleanupWorldRefused"), e.Path);
        }
        catch (Exception e) when (_controller.DescribeVpsFilesProblem(e) is { } text)
        {
            ErrorText = string.Format(Text.Get("Files_CleanupFailed"), text);
        }
        finally
        {
            IsCleaning = false;
        }

        // Whatever happened, the folder may have changed: list it again so the panel and the tree show what is left.
        await EvaluateCleanupAsync();
        CleanupStatus = status;
        _ = RefreshQuietlyAsync(browser, token);
    }

    private CleanupRules CleanupRulesNow() => new() { Backups = _app.BackupRules };

    private static FileOrganizer Organizer(WorkspaceBrowser browser) => new(browser.FileSystem, TimeProvider.System);

    private void NotifyCleanupChanged()
    {
        var selected = CleanupItems.Where(i => i.IsChecked).Select(i => i.Item).ToList();
        CleanupSummary = string.Format(Text.Get("Files_CleanupSummary"), selected.Count, CleanupItems.Count, ByteSize.Format(CleanupPlan.FreedBytes(selected)));
        CleanupHasRisk = selected.Any(i => i.Risky);
        OnPropertyChanged(nameof(ShowCleanupList));
        OnPropertyChanged(nameof(ShowCleanupEmpty));
        RunCleanupCommand.NotifyCanExecuteChanged();
    }
}
