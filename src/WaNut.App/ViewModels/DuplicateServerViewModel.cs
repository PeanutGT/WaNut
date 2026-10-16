using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WaNut.App.Resources;
using WaNut.Core.Instances;

namespace WaNut.App.ViewModels;

/// <summary>"Duplicate server": asks for a name, then copies the server without its backups.</summary>
public sealed partial class DuplicateServerViewModel(InstanceCopier copier) : ObservableObject
{
    private ServerViewModel _server = null!;
    private CancellationTokenSource? _cancellation;
    private bool _closeWhenCancelled;

    /// <summary>Raised when the window should close; true means a copy was made.</summary>
    public event Action<bool>? CloseRequested;

    /// <summary>The new server, once copied.</summary>
    public InstanceMetadata? Copy { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DuplicateCommand))]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(DuplicateCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private double _progressFraction;

    [ObservableProperty]
    private string? _errorText;

    public bool IsIdle => !IsBusy;

    public bool IsCopying => _cancellation is not null;

    public void Initialize(ServerViewModel server)
    {
        _server = server;
        Name = string.Format(Text.Get("Duplicate_DefaultName"), server.Name);
    }

    [RelayCommand(CanExecute = nameof(CanDuplicate))]
    private async Task DuplicateAsync()
    {
        IsBusy = true;
        ErrorText = null;
        ProgressFraction = 0;
        _cancellation = new CancellationTokenSource();
        try
        {
            Copy = await copier.CopyAsync(_server.Metadata, Name.Trim(), new Progress<double>(f => ProgressFraction = f), _cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            ProgressFraction = 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ErrorText = string.Format(Text.Get("Duplicate_Failed"), e.Message);
        }
        finally
        {
            _cancellation.Dispose();
            _cancellation = null;
            IsBusy = false;
        }

        if (Copy is not null)
        {
            CloseRequested?.Invoke(true);
        }
        else if (_closeWhenCancelled)
        {
            CloseRequested?.Invoke(false);
        }
    }

    private bool CanDuplicate() => IsIdle && !string.IsNullOrWhiteSpace(Name);

    [RelayCommand]
    private void Cancel()
    {
        if (IsCopying)
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
}
