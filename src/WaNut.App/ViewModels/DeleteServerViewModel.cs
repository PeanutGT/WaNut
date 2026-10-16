using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WaNut.App.Resources;
using WaNut.Core.Backups;
using WaNut.Core.Instances;
using WaNut.Core.Worlds;

namespace WaNut.App.ViewModels;

/// <summary>
/// "Delete server": names the server, offers to keep its world as a zip (on by default), then deletes its folder.
/// </summary>
public sealed partial class DeleteServerViewModel(InstanceDeleter deleter) : ObservableObject
{
    private ServerViewModel _server = null!;
    private CancellationTokenSource? _cancellation;
    private bool _closeWhenCancelled;

    /// <summary>Raised when the window should close; true means the server was deleted.</summary>
    public event Action<bool>? CloseRequested;

    /// <summary>Where the world zip went, or null when none was kept.</summary>
    public string? KeptZip { get; private set; }

    public string QuestionText { get; private set; } = string.Empty;

    /// <summary>A server that never started has no world, so there is nothing to keep.</summary>
    public bool HasWorld { get; private set; }

    public bool NoWorld => !HasWorld;

    [ObservableProperty]
    private bool _keepWorld = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private double _progressFraction;

    [ObservableProperty]
    private string? _errorText;

    public bool IsIdle => !IsBusy;

    public bool IsDeleting => _cancellation is not null;

    public void Initialize(ServerViewModel server)
    {
        _server = server;
        QuestionText = string.Format(Text.Get("Delete_Question"), server.Name);
        HasWorld = InstanceDeleter.HasWorld(server.InstanceDir);
        KeepWorld = HasWorld;
        OnPropertyChanged(string.Empty);
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task DeleteAsync()
    {
        IsBusy = true;
        ErrorText = null;
        ProgressFraction = 0;
        _cancellation = new CancellationTokenSource();
        var deleted = false;
        try
        {
            KeptZip = await deleter.DeleteAsync(_server.Metadata, KeepWorld && HasWorld, new Progress<double>(f => ProgressFraction = f), _cancellation.Token);
            deleted = true;
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
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            ErrorText = string.Format(Text.Get("Delete_Failed"), e.Message);
        }
        finally
        {
            _cancellation.Dispose();
            _cancellation = null;
            IsBusy = false;
        }

        if (deleted)
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
        if (IsDeleting)
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
