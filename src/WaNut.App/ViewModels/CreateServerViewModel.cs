using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WaNut.App.Resources;
using WaNut.Core.Downloads;
using WaNut.Core.Hosting;
using WaNut.Core.Instances;
using WaNut.Core.Versions;

namespace WaNut.App.ViewModels;

public sealed partial class CreateServerViewModel : ObservableObject
{
    private readonly MojangVersionService _versions;
    private readonly VanillaServerCreator _creator;
    private CancellationTokenSource? _cancellation;

    public CreateServerViewModel(MojangVersionService versions, VanillaServerCreator creator)
    {
        _versions = versions;
        _creator = creator;

        var totalMb = MemoryLimits.TotalPhysicalMb();
        MaxMemoryMb = MemoryLimits.MaxServerMb(totalMb);
        _memoryMb = MemoryLimits.DefaultServerMb(totalMb);
    }

    /// <summary>Raised when the wizard should close; true means a server was created.</summary>
    public event Action<bool>? CloseRequested;

    public ObservableCollection<GameVersion> Versions { get; } = [];

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

    public bool IsCreating => _cancellation is not null;

    public double ProgressPercent => (ProgressFraction ?? 0) * 100;

    public bool IsProgressIndeterminate => ProgressFraction is null;

    public string MemoryText => string.Format(Text.Get("CreateServer_MemoryValue"), MemoryMb / 1024.0);

    [RelayCommand]
    private async Task LoadVersionsAsync()
    {
        IsBusy = true;
        ErrorText = null;
        StatusText = Text.Get("CreateServer_LoadingVersions");
        try
        {
            var releases = await _versions.GetReleasesAsync();
            Versions.Clear();
            foreach (var release in releases)
            {
                Versions.Add(release);
            }

            SelectedVersion = Versions.FirstOrDefault();
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

    private bool CanCreate() => IsIdle && EulaAccepted && SelectedVersion is not null && !string.IsNullOrWhiteSpace(Name);

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
                new CreateServerRequest(Name, SelectedVersion!, MemoryMb, EulaAccepted),
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
