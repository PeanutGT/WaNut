using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WaNut.App.Resources;
using WaNut.Core.Backups;
using WaNut.Core.Downloads;
using WaNut.Core.Instances;
using WaNut.Core.Settings;
using WaNut.Core.Versions;

namespace WaNut.App.ViewModels;

/// <summary>
/// The "Change type or version" window. The world is backed up first; going to an older version needs an extra
/// confirmation, because a world saved by a newer version can break when an older one opens it.
/// </summary>
public sealed partial class ChangeServerViewModel : ObservableObject
{
    private readonly ServerVersionCatalog _catalog;
    private readonly ServerCreator _creator;
    private CancellationTokenSource? _cancellation;
    private InstanceMetadata _server = null!;
    private bool _worldExists;
    private bool _closeWhenCancelled;

    public ChangeServerViewModel(ServerVersionCatalog catalog, ServerCreator creator)
    {
        _catalog = catalog;
        _creator = creator;
    }

    /// <summary>Raised when the window should close; true means the server was changed.</summary>
    public event Action<bool>? CloseRequested;

    public IReadOnlyList<ServerTypeOption> ServerTypes { get; } = ServerTypeOption.All();

    public ObservableCollection<GameVersion> Versions { get; } = [];

    public InstanceMetadata? Changed { get; private set; }

    public string CurrentText { get; private set; } = string.Empty;

    public string DowngradeWarningText { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUnchanged), nameof(IsDowngrade), nameof(ShowDowngradeWarning), nameof(ShowModsMovedNote), nameof(ModsMovedText))]
    [NotifyCanExecuteChangedFor(nameof(ChangeCommand))]
    private ServerTypeOption? _selectedType;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUnchanged), nameof(IsDowngrade), nameof(ShowDowngradeWarning))]
    [NotifyCanExecuteChangedFor(nameof(ChangeCommand))]
    private GameVersion? _selectedVersion;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChangeCommand))]
    private bool _downgradeAccepted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(ChangeCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string? _statusText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressPercent), nameof(IsProgressIndeterminate))]
    private double? _progressFraction;

    [ObservableProperty]
    private string? _errorText;

    /// <summary>Shown after the user stopped a change, so it is clear that nothing was changed.</summary>
    [ObservableProperty]
    private bool _wasCancelled;

    public bool IsIdle => !IsBusy;

    public bool IsChanging => _cancellation is not null;

    /// <summary>Once files are being replaced, stopping halfway would leave a broken server, so it can no longer be cancelled.</summary>
    public bool CanCancelChange { get; private set; }

    public double ProgressPercent => (ProgressFraction ?? 0) * 100;

    public bool IsProgressIndeterminate => ProgressFraction is null;

    public bool IsUnchanged => SelectedType?.Type == _server?.Type && SelectedVersion?.Id == _server?.MinecraftVersion;

    public bool IsDowngrade => SelectedVersion is not null && _server is not null && ServerCreator.IsDowngrade(_server, SelectedVersion.Id);

    /// <summary>Only a world that exists can be harmed by an older version.</summary>
    public bool ShowDowngradeWarning => IsDowngrade && _worldExists;

    /// <summary>One mod loader can't load another's mods, so the change renames the mods folder.</summary>
    public bool ShowModsMovedNote => _server is not null && SelectedType is not null && ServerCreator.MovesMods(_server.Type, SelectedType.Type);

    public string ModsMovedText => ShowModsMovedNote
        ? string.Format(Text.Get("Change_ModsMoved"), _server!.Type, SelectedType!.Title, $"mods-{_server.Type.ToString().ToLowerInvariant()}")
        : string.Empty;

    public void Initialize(InstanceMetadata server, string instanceDir)
    {
        // The type is picked before _server is set, so this does not start a second version load; the window's
        // Loaded event starts the first one.
        SelectedType = ServerTypes.First(t => t.Type == server.Type);
        _server = server;
        _worldExists = GameSettingsStore.WorldExists(instanceDir);
        CurrentText = $"{server.Type} {server.MinecraftVersion}";
        DowngradeWarningText = string.Format(Text.Get("Change_DowngradeWarning"), CurrentText);
    }

    partial void OnSelectedTypeChanged(ServerTypeOption? value)
    {
        if (!LoadVersionsCommand.IsRunning && _server is not null)
        {
            LoadVersionsCommand.Execute(null);
        }
    }

    partial void OnSelectedVersionChanged(GameVersion? value) => DowngradeAccepted = false;

    [RelayCommand]
    private async Task LoadVersionsAsync()
    {
        SelectedType ??= ServerTypes[0];
        IsBusy = true;
        ErrorText = null;
        StatusText = Text.Get("CreateServer_LoadingVersions");
        try
        {
            ServerType type;
            IReadOnlyList<GameVersion> releases;
            do
            {
                type = SelectedType.Type;
                releases = await _catalog.GetVersionsAsync(type);
            }
            while (type != SelectedType.Type);

            // Start from the server's own version, so changing only the type is one click.
            var previous = SelectedVersion?.Id ?? _server.MinecraftVersion;
            Versions.Clear();
            foreach (var release in releases)
            {
                Versions.Add(release);
            }

            SelectedVersion = Versions.FirstOrDefault(v => v.Id == previous) ?? Versions.FirstOrDefault();
            StatusText = null;
        }
        catch (Exception)
        {
            // Offline, an API unreachable or an unexpected reply: let the user retry instead of crashing.
            StatusText = null;
            ErrorText = Text.Get("CreateServer_VersionsFailed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanChange() =>
        IsIdle && SelectedType is not null && SelectedVersion is not null && !IsUnchanged && (!ShowDowngradeWarning || DowngradeAccepted);

    [RelayCommand(CanExecute = nameof(CanChange))]
    private async Task ChangeAsync()
    {
        IsBusy = true;
        ErrorText = null;
        WasCancelled = false;
        _cancellation = new CancellationTokenSource();
        CanCancelChange = true;
        OnPropertyChanged(nameof(CanCancelChange));
        try
        {
            Changed = await _creator.ChangeAsync(_server, SelectedType!.Type, SelectedVersion!, new Progress<CreateServerProgress>(ShowProgress), _cancellation.Token);
        }
        catch (Exception) when (_cancellation.IsCancellationRequested)
        {
            // A cancelled download can surface as another exception type; either way nothing was changed.
            StatusText = null;
            ProgressFraction = null;
            WasCancelled = true;
        }
        catch (DownloadVerificationException)
        {
            ErrorText = Text.Get("CreateServer_ChecksumFailed");
        }
        catch (BackupException e)
        {
            ErrorText = string.Format(Text.Get("Change_BackupFailed"), Text.Get($"Backups_Problem_{e.Problem}"));
        }
        catch (Exception e)
        {
            ErrorText = string.Format(Text.Get("Change_Failed"), e.Message);
        }
        finally
        {
            _cancellation.Dispose();
            _cancellation = null;
            CanCancelChange = false;
            OnPropertyChanged(nameof(CanCancelChange));
            IsBusy = false;
        }

        if (Changed is not null)
        {
            CloseRequested?.Invoke(true);
        }
        else if (_closeWhenCancelled)
        {
            CloseRequested?.Invoke(false);
        }
    }

    /// <summary>Stops a change that has not started replacing files yet, or closes the window when idle.</summary>
    [RelayCommand]
    private void Cancel()
    {
        if (IsChanging)
        {
            CancelChange();
            return;
        }

        CloseRequested?.Invoke(false);
    }

    public void CancelChange()
    {
        if (CanCancelChange)
        {
            _cancellation?.Cancel();
        }
    }

    /// <summary>
    /// The window's close button during a change: stops it and closes once it has stopped. While files are being
    /// replaced the change cannot stop, so the window closes when it finishes instead.
    /// </summary>
    public void CancelAndClose()
    {
        _closeWhenCancelled = true;
        CancelChange();
    }

    private void ShowProgress(CreateServerProgress progress)
    {
        if (progress.Stage == CreateServerStage.WritingFiles && CanCancelChange)
        {
            CanCancelChange = false;
            OnPropertyChanged(nameof(CanCancelChange));
        }

        StatusText = Text.Get($"CreateServer_Stage_{progress.Stage}");
        ProgressFraction = progress.Fraction;
    }
}
