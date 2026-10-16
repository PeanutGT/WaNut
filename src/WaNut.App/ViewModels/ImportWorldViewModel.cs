using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WaNut.App.Resources;
using WaNut.App.Services;
using WaNut.Core.Backups;
using WaNut.Core.Players;
using WaNut.Core.Worlds;

namespace WaNut.App.ViewModels;

/// <summary>
/// "Replace with another world" on the Backups tab. The server's current world is backed up first, like a
/// restore, so the replacement can be undone from the backup list.
/// </summary>
public sealed partial class ImportWorldViewModel : ObservableObject
{
    private readonly WorldImporter _importer;
    private ServerViewModel _server = null!;
    private CancellationTokenSource? _cancellation;
    private bool _closeWhenCancelled;

    public ImportWorldViewModel(WorldImporter importer, IDialogService dialogs)
    {
        _importer = importer;
        Picker = new WorldPickerViewModel(dialogs);
        Picker.SelectionChanged += () =>
        {
            NewerWorldAccepted = false;
            OnPropertyChanged(nameof(ShowNewerWorldWarning));
            OnPropertyChanged(nameof(NewerWorldWarningText));
            ImportCommand.NotifyCanExecuteChanged();
        };
        Picker.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WorldPickerViewModel.ModdedAccepted))
            {
                ImportCommand.NotifyCanExecuteChanged();
            }
        };
    }

    /// <summary>Raised when the window should close; true means the world was replaced.</summary>
    public event Action<bool>? CloseRequested;

    public WorldPickerViewModel Picker { get; }

    public WorldImportResult? Result { get; private set; }

    public string ServerText { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OwnerNameInvalid), nameof(CanBringInventory))]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private string _ownerName = string.Empty;

    [ObservableProperty]
    private bool _bringInventory = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private bool _newerWorldAccepted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private double _progressFraction;

    [ObservableProperty]
    private string? _errorText;

    public bool IsIdle => !IsBusy;

    public bool IsImporting => _cancellation is not null;

    public bool OwnerNameInvalid => OwnerName.Trim().Length > 0 && !PlayerLists.IsValidName(OwnerName.Trim());

    public bool CanBringInventory => OwnerName.Trim().Length > 0 && !OwnerNameInvalid;

    public bool ShowNewerWorldWarning => Picker.IsNewerThan(_server?.Metadata.MinecraftVersion);

    public string NewerWorldWarningText => string.Format(Text.Get("World_NewerServerWarning"), Picker.Source?.Details.VersionName, _server?.Metadata.MinecraftVersion);

    public void Initialize(ServerViewModel server)
    {
        _server = server;
        ServerText = $"{server.Name} ({server.TypeAndVersionText})";

        // The first operator is usually the host: the wizard adds the owner's name there.
        OwnerName = PlayerLists.ReadNames(server.InstanceDir, PlayerList.Operators).FirstOrDefault() ?? string.Empty;
        OnPropertyChanged(nameof(ServerText));
    }

    private bool CanImport() => IsIdle && Picker.IsAccepted && !OwnerNameInvalid && (!ShowNewerWorldWarning || NewerWorldAccepted);

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportAsync()
    {
        IsBusy = true;
        ErrorText = null;
        ProgressFraction = 0;
        _cancellation = new CancellationTokenSource();
        try
        {
            var metadata = _server.Metadata;
            var request = new WorldImportRequest(Picker.Source!, metadata.Type, metadata.MinecraftVersion, OwnerName.Trim(), BringInventory && CanBringInventory);
            Result = await _importer.ImportAsync(_server.InstanceDir, request, new Progress<double>(f => ProgressFraction = f), _cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            ProgressFraction = 0;
        }
        catch (WorldException e)
        {
            ErrorText = WorldMessages.Problem(e);
        }
        catch (BackupException e)
        {
            ErrorText = string.Format(Text.Get("Change_BackupFailed"), Text.Get($"Backups_Problem_{e.Problem}"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            ErrorText = string.Format(Text.Get("World_ImportFailed"), e.Message);
        }
        finally
        {
            _cancellation.Dispose();
            _cancellation = null;
            IsBusy = false;
        }

        if (Result is not null)
        {
            CloseRequested?.Invoke(true);
        }
        else if (_closeWhenCancelled)
        {
            CloseRequested?.Invoke(false);
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        if (IsImporting)
        {
            _cancellation?.Cancel();
            return;
        }

        CloseRequested?.Invoke(false);
    }

    /// <summary>The window's close button during an import: stops it, and closes once nothing was changed.</summary>
    public void CancelAndClose()
    {
        _closeWhenCancelled = true;
        _cancellation?.Cancel();
    }
}
