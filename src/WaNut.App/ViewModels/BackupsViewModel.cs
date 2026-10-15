using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WaNut.App.Resources;
using WaNut.App.Services;
using WaNut.Core.Backups;
using WaNut.Core.Hosting;

namespace WaNut.App.ViewModels;

/// <summary>
/// The Backups tab: the list of world backups, "Back up now" and restore. Backups and restores only run while
/// the server is stopped, because a running server keeps writing its world.
/// </summary>
public sealed partial class BackupsViewModel : ObservableObject
{
    private readonly ServerViewModel _server;
    private readonly BackupStore _store;
    private readonly IDialogService _dialogs;

    public BackupsViewModel(ServerViewModel server, BackupStore store, AppSettingsViewModel app, IDialogService dialogs)
    {
        _server = server;
        _store = store;
        _dialogs = dialogs;
        App = app;
        Reload();
    }

    /// <summary>For the keep count and age rules, which every server shares.</summary>
    public AppSettingsViewModel App { get; }

    public ObservableCollection<BackupItemViewModel> Items { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWorking), nameof(CanChange), nameof(WorkText))]
    [NotifyCanExecuteChangedFor(nameof(BackUpNowCommand), nameof(RestoreCommand))]
    private BackupReason? _working;

    [ObservableProperty]
    private double _workFraction;

    [ObservableProperty]
    private string? _errorText;

    [ObservableProperty]
    private string? _doneText;

    public bool IsWorking => Working is not null;

    /// <summary>Backing up and restoring wait until the server is stopped and nothing else is running.</summary>
    public bool CanChange => _server.Status == ServerStatus.Stopped && !_server.IsPreparing && !IsWorking;

    public bool ShowStopToChange => _server.IsRunning;

    public string WorkText => Working == BackupReason.Restore ? Text.Get("Backups_Restoring") : Text.Get("Backups_Working");

    public string TotalSizeText => string.Format(Text.Get("Backups_Total"), Items.Count, BackupItemViewModel.FormatSize(Items.Sum(i => i.Info.SizeBytes)));

    public void Reload()
    {
        Items.Clear();
        try
        {
            foreach (var backup in _store.List(_server.InstanceDir))
            {
                Items.Add(new BackupItemViewModel(backup));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ErrorText = string.Format(Text.Get("Backups_ListFailed"), e.Message);
        }

        OnPropertyChanged(nameof(TotalSizeText));
    }

    internal void OnServerStatusChanged()
    {
        OnPropertyChanged(nameof(CanChange));
        OnPropertyChanged(nameof(ShowStopToChange));
        BackUpNowCommand.NotifyCanExecuteChanged();
        RestoreCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanChange))]
    private async Task BackUpNowAsync()
    {
        await RunAsync(BackupReason.Manual, async progress =>
        {
            var backup = await _store.CreateAsync(_server.InstanceDir, BackupReason.Manual, progress);
            if (backup is null)
            {
                return Text.Get("Backups_NoWorldYet");
            }

            _store.Cleanup(_server.InstanceDir, App.BackupRules);
            return Text.Get("Backups_Done");
        });
    }

    [RelayCommand(CanExecute = nameof(CanRestore))]
    private async Task RestoreAsync(BackupItemViewModel? item)
    {
        if (item is null || !_dialogs.Confirm(string.Format(Text.Get("Backups_RestoreConfirm"), _server.Name, item.DateText)))
        {
            return;
        }

        await RunAsync(BackupReason.Restore, async progress =>
        {
            await _store.RestoreAsync(_server.InstanceDir, item.Info, progress);
            return string.Format(Text.Get("Backups_Restored"), item.DateText);
        });
    }

    private bool CanRestore(BackupItemViewModel? item) => item is not null && CanChange;

    [RelayCommand]
    private void OpenFolder()
    {
        var dir = BackupStore.BackupsDir(_server.InstanceDir);
        try
        {
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            ErrorText = string.Format(Text.Get("Backups_OpenFolderFailed"), e.Message);
        }
    }

    private async Task RunAsync(BackupReason reason, Func<IProgress<double>, Task<string>> work)
    {
        ErrorText = null;
        DoneText = null;
        WorkFraction = 0;
        Working = reason;
        _server.OnBackupWorkChanged();
        try
        {
            DoneText = await work(new Progress<double>(f => WorkFraction = f));
        }
        catch (BackupException e)
        {
            ErrorText = Text.Get($"Backups_Problem_{e.Problem}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ErrorText = string.Format(Text.Get(reason == BackupReason.Restore ? "Backups_RestoreFailed" : "Backups_Failed"), e.Message);
        }
        finally
        {
            Working = null;
            _server.OnBackupWorkChanged();
            Reload();
        }
    }
}
