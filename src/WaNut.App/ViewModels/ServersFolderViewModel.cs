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
        Directory.CreateDirectory(paths.InstancesDir);
        Process.Start(new ProcessStartInfo(paths.InstancesDir) { UseShellExecute = true });
    }

    private bool CanMove() => !IsMoving;

    [RelayCommand(CanExecute = nameof(CanMove))]
    private async Task MoveAsync()
    {
        ErrorText = null;
        if (controller.HasRunningServer)
        {
            ErrorText = Text.Get("ServersFolder_StopFirst");
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
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StatusText = null;
            ErrorText = string.Format(Text.Get("ServersFolder_Failed"), e.Message);
            IsMoving = false;
        }
    }
}
