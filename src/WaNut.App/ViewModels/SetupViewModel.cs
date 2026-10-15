using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using WaNut.App.Services;
using WaNut.Core;
using WaNut.Core.Settings;

namespace WaNut.App.ViewModels;

/// <summary>The window shown the first time WaNut opens: desktop shortcut, servers folder and update check.</summary>
public sealed partial class SetupViewModel : ObservableObject
{
    private readonly LauncherPaths _paths;
    private readonly AppSettingsStore _store;

    public SetupViewModel(LauncherPaths paths, AppSettingsStore store)
    {
        _paths = paths;
        _store = store;
        _instancesDir = paths.InstancesDir;

        // Someone updating from 0.1.0 may already have servers; moving them is not this window's job.
        CanChooseFolder = !Directory.Exists(paths.InstancesDir) || !Directory.EnumerateDirectories(paths.InstancesDir).Any();
    }

    public bool CanChooseFolder { get; }

    [ObservableProperty]
    private bool _desktopShortcut = true;

    [ObservableProperty]
    private bool _checkForUpdates = true;

    [ObservableProperty]
    private string _instancesDir;

    [ObservableProperty]
    private string? _errorText;

    /// <summary>Raised after the choices are saved, so the window can close.</summary>
    public event EventHandler? Finished;

    [RelayCommand]
    private void Browse()
    {
        var dialog = new OpenFolderDialog { InitialDirectory = InstancesDir };
        if (dialog.ShowDialog() == true)
        {
            // A folder of its own, so WaNut never mixes servers into a folder the user already uses.
            InstancesDir = Path.Combine(dialog.FolderName, "WaNut servers");
        }
    }

    [RelayCommand]
    private void Finish()
    {
        try
        {
            var defaultDir = _paths.WithInstancesDir(null).InstancesDir;
            var chosen = string.Equals(Path.GetFullPath(InstancesDir), defaultDir, StringComparison.OrdinalIgnoreCase) ? null : InstancesDir;
            Directory.CreateDirectory(InstancesDir);

            if (DesktopShortcut)
            {
                Services.DesktopShortcut.Create();
            }
            else
            {
                Services.DesktopShortcut.Remove();
            }

            _store.Save(_store.Load() with { SetupDone = true, CheckForUpdates = CheckForUpdates, InstancesDir = chosen });
            Finished?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            ErrorText = string.Format(Resources.Text.Get("Setup_Failed"), e.Message);
        }
    }
}
