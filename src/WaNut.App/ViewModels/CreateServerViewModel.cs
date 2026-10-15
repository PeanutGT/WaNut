using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WaNut.App.Resources;
using WaNut.Core.Downloads;
using WaNut.Core.Hosting;
using WaNut.Core.Instances;
using WaNut.Core.Players;
using WaNut.Core.Versions;

namespace WaNut.App.ViewModels;

public sealed partial class CreateServerViewModel : ObservableObject
{
    private readonly ServerVersionCatalog _catalog;
    private readonly ServerCreator _creator;
    private CancellationTokenSource? _cancellation;

    public CreateServerViewModel(ServerVersionCatalog catalog, ServerCreator creator)
    {
        _catalog = catalog;
        _creator = creator;

        var totalMb = MemoryLimits.TotalPhysicalMb();
        MaxMemoryMb = MemoryLimits.MaxServerMb(totalMb);
        _memoryMb = MemoryLimits.DefaultServerMb(totalMb);
    }

    /// <summary>Raised when the wizard should close; true means a server was created.</summary>
    public event Action<bool>? CloseRequested;

    public ObservableCollection<GameVersion> Versions { get; } = [];

    public IReadOnlyList<ServerTypeOption> ServerTypes { get; } =
    [
        new(ServerType.Vanilla, Text.Get("CreateServer_TypeVanilla"), Text.Get("CreateServer_TypeVanillaHint")),
        new(ServerType.Paper, Text.Get("CreateServer_TypePaper"), Text.Get("CreateServer_TypePaperHint")),
        new(ServerType.Fabric, Text.Get("CreateServer_TypeFabric"), Text.Get("CreateServer_TypeFabricHint")),
    ];

    [ObservableProperty]
    private ServerTypeOption? _selectedType;

    public int MinMemoryMb => MemoryLimits.MinimumMb;

    public int MaxMemoryMb { get; }

    public int MemoryStepMb => MemoryLimits.StepMb;

    public InstanceMetadata? Created { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string _name = Text.Get("CreateServer_DefaultName");

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private GameVersion? _selectedVersion;

    /// <summary>The host's Minecraft name; empty skips it.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    [NotifyPropertyChangedFor(nameof(OwnerNameInvalid))]
    private string _ownerName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MemoryText))]
    private int _memoryMb;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private bool _eulaAccepted;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isBusy;

    [ObservableProperty]
    private string? _statusText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressPercent), nameof(IsProgressIndeterminate))]
    private double? _progressFraction;

    [ObservableProperty]
    private string? _errorText;

    public bool IsIdle => !IsBusy;

    public bool OwnerNameInvalid => OwnerName.Trim().Length > 0 && !PlayerLists.IsValidName(OwnerName.Trim());

    public bool IsCreating => _cancellation is not null;

    public double ProgressPercent => (ProgressFraction ?? 0) * 100;

    public bool IsProgressIndeterminate => ProgressFraction is null;

    public string MemoryText => string.Format(Text.Get("CreateServer_MemoryValue"), MemoryMb / 1024.0);

    partial void OnSelectedTypeChanged(ServerTypeOption? value)
    {
        // A load already running picks up the new type when it finishes.
        if (!LoadVersionsCommand.IsRunning)
        {
            LoadVersionsCommand.Execute(null);
        }
    }

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

            // Keep the chosen version when the new type offers it too.
            var previous = SelectedVersion?.Id;
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
            // Offline, Mojang unreachable or an unexpected reply: let the user retry instead of crashing.
            StatusText = null;
            ErrorText = Text.Get("CreateServer_VersionsFailed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanCreate() => IsIdle && EulaAccepted && SelectedType is not null && SelectedVersion is not null && !string.IsNullOrWhiteSpace(Name) && !OwnerNameInvalid;

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateAsync()
    {
        IsBusy = true;
        ErrorText = null;
        _cancellation = new CancellationTokenSource();
        var progress = new Progress<CreateServerProgress>(ShowProgress);
        try
        {
            Created = await _creator.CreateAsync(
                new CreateServerRequest(Name, SelectedVersion!, MemoryMb, EulaAccepted, SelectedType!.Type, OwnerName),
                progress,
                _cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            StatusText = null;
            ProgressFraction = null;
        }
        catch (DownloadVerificationException)
        {
            ErrorText = Text.Get("CreateServer_ChecksumFailed");
        }
        catch (Exception ex)
        {
            ErrorText = string.Format(Text.Get("CreateServer_Failed"), ex.Message);
        }
        finally
        {
            _cancellation.Dispose();
            _cancellation = null;
            IsBusy = false;
        }

        // Only after IsCreating is false, so the window does not treat this close as "stop the download".
        if (Created is not null)
        {
            CloseRequested?.Invoke(true);
        }
    }

    /// <summary>Stops a running creation, or closes the wizard when nothing is being created.</summary>
    [RelayCommand]
    private void Cancel()
    {
        if (IsCreating)
        {
            CancelCreation();
            return;
        }

        CloseRequested?.Invoke(false);
    }

    public void CancelCreation() => _cancellation?.Cancel();

    [RelayCommand]
    private void OpenEula()
    {
        Process.Start(new ProcessStartInfo(EulaFile.EulaUrl.AbsoluteUri) { UseShellExecute = true });
    }

    private void ShowProgress(CreateServerProgress progress)
    {
        StatusText = Text.Get($"CreateServer_Stage_{progress.Stage}");
        ProgressFraction = progress.Fraction;
    }
}

/// <summary>One choice in the wizard's server type list.</summary>
public sealed record ServerTypeOption(ServerType Type, string Title, string Description)
{
    /// <summary>The pixel icon for this type, e.g. "type-paper".</summary>
    public string Scene => $"type-{Type.ToString().ToLowerInvariant()}";
}
