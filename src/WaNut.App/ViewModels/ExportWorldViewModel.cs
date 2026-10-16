using System.Globalization;
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
/// "Export world": the current world or one backup, as a .zip or straight into a launcher's single-player saves.
/// One player's inventory and position can become the single-player host's.
/// </summary>
public sealed partial class ExportWorldViewModel : ObservableObject
{
    private readonly WorldExporter _exporter;
    private readonly IDialogService _dialogs;
    private ServerViewModel _server = null!;
    private BackupInfo? _backup;
    private CancellationTokenSource? _cancellation;
    private bool _closeWhenCancelled;

    public ExportWorldViewModel(WorldExporter exporter, IDialogService dialogs)
    {
        _exporter = exporter;
        _dialogs = dialogs;
        _selectedTarget = Targets[0];
    }

    /// <summary>Raised when the window should close; true means the world was exported.</summary>
    public event Action<bool>? CloseRequested;

    public IReadOnlyList<WorldChoice> Targets { get; } =
    [
        new(nameof(ExportTarget.Zip), Text.Get("Export_ToZip"), Text.Get("Export_ToZipHint")),
        new(nameof(ExportTarget.Saves), Text.Get("Export_ToSaves"), Text.Get("Export_ToSavesHint")),
    ];

    public IReadOnlyList<SaveLocationOption> Locations { get; private set; } = [];

    public IReadOnlyList<KnownPlayer> Players { get; private set; } = [];

    public WorldExportResult? Result { get; private set; }

    public string SourceText { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSaves))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private WorldChoice _selectedTarget;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private SaveLocationOption? _selectedLocation;

    [ObservableProperty]
    private bool _bringInventory = true;

    [ObservableProperty]
    private KnownPlayer? _selectedPlayer;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private double _progressFraction;

    [ObservableProperty]
    private string? _errorText;

    public bool IsIdle => !IsBusy;

    public bool IsExporting => _cancellation is not null;

    public bool IsSaves => SelectedTarget.Id == nameof(ExportTarget.Saves);

    public bool HasPlayers => Players.Count > 0;

    public bool NoPlayers => !HasPlayers;

    public bool NoLocations => Locations.Count == 0;

    public void Initialize(ServerViewModel server, BackupInfo? backup)
    {
        _server = server;
        _backup = backup;
        SourceText = backup is null
            ? Text.Get("Export_CurrentWorld")
            : string.Format(Text.Get("Export_Backup"), backup.CreatedAt.ToLocalTime().ToString(Text.Get("Backups_DateFormat"), CultureInfo.InvariantCulture));
        Locations = SaveLocations.FindDefault().Select(l => new SaveLocationOption(l)).ToList();
        SelectedLocation = Locations.FirstOrDefault();

        // The host is picked by default: the first operator, which the wizard sets to the owner's name.
        Players = UserCache.Read(server.InstanceDir);
        var host = PlayerLists.ReadNames(server.InstanceDir, PlayerList.Operators).FirstOrDefault();
        SelectedPlayer = Players.FirstOrDefault(p => string.Equals(p.Name, host, StringComparison.OrdinalIgnoreCase)) ?? Players.FirstOrDefault();
        BringInventory = HasPlayers;
        OnPropertyChanged(string.Empty);
    }

    private bool CanExport() => IsIdle && (!IsSaves || SelectedLocation is not null);

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        string destination;
        if (IsSaves)
        {
            destination = SelectedLocation!.Location.SavesDir;
        }
        else if (_dialogs.PickZipToSave(WorldFileName() + ".zip") is { } zip)
        {
            destination = zip;
        }
        else
        {
            return;
        }

        IsBusy = true;
        ErrorText = null;
        ProgressFraction = 0;
        _cancellation = new CancellationTokenSource();
        try
        {
            var target = IsSaves ? ExportTarget.Saves : ExportTarget.Zip;
            var player = BringInventory ? SelectedPlayer?.Uuid : null;
            Result = await _exporter.ExportAsync(
                _server.InstanceDir, new WorldExportRequest(_backup, target, destination, _server.Name, player), new Progress<double>(f => ProgressFraction = f), _cancellation.Token);
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
            ErrorText = Text.Get($"Backups_Problem_{e.Problem}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            ErrorText = string.Format(Text.Get("Export_Failed"), e.Message);
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
        if (IsExporting)
        {
            _cancellation?.Cancel();
            return;
        }

        CloseRequested?.Invoke(false);
    }

    public void CancelAndClose()
    {
        _closeWhenCancelled = true;
        _cancellation?.Cancel();
    }

    private string WorldFileName()
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = new string(_server.Name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return name.Length == 0 ? "world" : name;
    }
}

/// <summary>A saves folder in the export list: "Minecraft Launcher", "Prism Launcher · Create Pack".</summary>
public sealed record SaveLocationOption(SaveLocation Location)
{
    public string Title => Location.InstanceName is null
        ? WorldOptionViewModel.LauncherName(Location.Launcher)
        : $"{WorldOptionViewModel.LauncherName(Location.Launcher)} · {Location.InstanceName}";

    public override string ToString() => Title;
}
