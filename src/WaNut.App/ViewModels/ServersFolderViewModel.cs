using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using WaNut.App.Resources;
using WaNut.App.Services;
using WaNut.Core;
using WaNut.Core.Instances;
using WaNut.Core.Settings;

namespace WaNut.App.ViewModels;

/// <summary>The servers folder in App settings: shows where servers live and moves them all to another folder.</summary>
public sealed partial class ServersFolderViewModel(LauncherPaths paths, AppSettingsStore store, ServerController controller, IDialogService dialogs)
    : ObservableObject
{
    public string InstancesDir => paths.InstancesDir;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MoveCommand))]
    private bool _isMoving;

    [ObservableProperty]
    private string? _statusText;

    [ObservableProperty]
    private string? _errorText;

    [RelayCommand]
    private void OpenFolder()
    {
        ErrorText = null;
        try
        {
            Directory.CreateDirectory(paths.InstancesDir);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ErrorText = string.Format(Text.Get("ServersFolder_Failed"), e.Message);
            return;
        }

        if (!ShellOpener.TryOpen(paths.InstancesDir))
        {
            ErrorText = string.Format(Text.Get("Shell_OpenFailed"), paths.InstancesDir);
        }
    }

    /// <summary>Why the folder can't move right now: every server's files must be at rest first.</summary>
    private string? Blocker() =>
        controller.HasRunningServer ? Text.Get("ServersFolder_StopFirst")
        : controller.IsAnyPreparing || controller.IsSyncingBack ? Text.Get("ServersFolder_BusyFirst")
        : null;

    private bool CanMove() => !IsMoving;

    [RelayCommand(CanExecute = nameof(CanMove))]
    private async Task MoveAsync()
    {
        ErrorText = null;
        if (Blocker() is { } blocker)
        {
            ErrorText = blocker;
            return;
        }

        var dialog = new OpenFolderDialog();
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        // A folder of its own, as in the first-run setup, so servers never mix with the user's files.
        var target = Path.Combine(dialog.FolderName, "WaNut servers");
        if (InstancesFolderMover.Check(paths.InstancesDir, target) is { } problem)
        {
            ErrorText = Text.Get($"ServersFolder_{problem}");
            return;
        }

        if (!dialogs.Confirm(string.Format(Text.Get("ServersFolder_Confirm"), target)))
        {
            return;
        }

        // The folder picker and the question take a while; a server may have been started from the tray meanwhile.
        if (Blocker() is { } blockerNow)
        {
            ErrorText = blockerNow;
            return;
        }

        IsMoving = true;
        try
        {
            var progress = new Progress<string>(name => StatusText = string.Format(Text.Get("ServersFolder_Moving"), name));
            var from = paths.InstancesDir;
            await Task.Run(() => InstancesFolderMover.Move(from, target, progress));

            var isDefault = string.Equals(Path.GetFullPath(target), paths.WithInstancesDir(null).InstancesDir, StringComparison.OrdinalIgnoreCase);
            store.Save(store.Load() with { InstancesDir = isDefault ? null : target });

            // Every part of WaNut was set up with the old folder, so a fresh start is the simplest way to switch.
            StatusText = Text.Get("Update_Restarting");
            Process.Start(Environment.ProcessPath ?? throw new InvalidOperationException("WaNut.exe path is unknown."));
            Application.Current.Shutdown();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception)
        {
            StatusText = null;
            ErrorText = string.Format(Text.Get("ServersFolder_Failed"), e.Message);
            IsMoving = false;
        }
    }
}
