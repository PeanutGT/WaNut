using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Velopack;
using WaNut.App.Resources;
using WaNut.App.Services;
using WaNut.Core.Updates;

namespace WaNut.App.ViewModels;

/// <summary>
/// WaNut's own updates (decision 8): a quiet check when WaNut starts, a card in the sidebar and a Windows notification
/// when a new version is out, and "Check now" in App settings. Every check's result goes to update.log. A running server is stopped (and its world saved) before WaNut restarts.
/// </summary>
public sealed partial class UpdateViewModel : ObservableObject
{
    // Lets the window and the servers settle before going online.
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(5);

    private readonly AppUpdater _updater;
    private readonly ServerController _controller;
    private readonly IDialogService _dialogs;
    private readonly TrayIcon _tray;
    private readonly UpdateLog _log;
    private UpdateInfo? _update;

    public UpdateViewModel(AppUpdater updater, ServerController controller, IDialogService dialogs, TrayIcon tray, UpdateLog log)
    {
        _updater = updater;
        _controller = controller;
        _dialogs = dialogs;
        _tray = tray;
        _log = log;
        VersionText = string.Format(
            Text.Get("Update_CurrentVersion"),
            _updater.CurrentVersion ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0");
    }

    public string VersionText { get; }

    public bool IsInstalled => _updater.IsInstalled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCard))]
    private string? _availableVersion;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCard), nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(CheckNowCommand), nameof(UpdateNowCommand))]
    private bool _isWorking;

    [ObservableProperty]
    private int _downloadPercent;

    [ObservableProperty]
    private string? _statusText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCard))]
    private bool _dismissed;

    public bool IsIdle => !IsWorking;

    /// <summary>The sidebar card: shown while a new version waits, until the user picks "Later".</summary>
    public bool ShowCard => AvailableVersion is not null && (!Dismissed || IsWorking);

    public string CardText => string.Format(Text.Get("Update_Available"), AvailableVersion);

    /// <summary>Called once when WaNut starts, if the user left update checks on.</summary>
    public async Task CheckOnStartupAsync()
    {
        if (!IsInstalled)
        {
            _log.Write("Startup check skipped: WaNut is not running from its installer.");
            return;
        }

        await Task.Delay(StartupDelay);
        await CheckAsync(quiet: true);
        if (AvailableVersion is not null)
        {
            _tray.Notify(Text.Get("App_Title"), string.Format(Text.Get("Update_Notify"), AvailableVersion));
        }
    }

    private bool CanCheck() => IsIdle;

    [RelayCommand(CanExecute = nameof(CanCheck))]
    private Task CheckNowAsync() => CheckAsync(quiet: false);

    private bool CanUpdate() => IsIdle && _update is not null;

    [RelayCommand(CanExecute = nameof(CanUpdate))]
    private async Task UpdateNowAsync()
    {
        if (_update is not { } update)
        {
            return;
        }

        if (!await StopServerForUpdateAsync())
        {
            return;
        }

        IsWorking = true;
        DownloadPercent = 0;
        StatusText = Text.Get("Update_Downloading");
        try
        {
            await _updater.DownloadAsync(update, new Progress<int>(p => DownloadPercent = p));

            // A server may have been started while the update downloaded; restarting must not cut it off.
            StatusText = null;
            if (!await StopServerForUpdateAsync())
            {
                return;
            }

            StatusText = Text.Get("Update_Restarting");
            _updater.ApplyAndRestart(update);
        }
        catch (Exception e)
        {
            // Network errors, a full disk, and Velopack's own checksum and feed exceptions all end here.
            StatusText = string.Format(Text.Get("Update_Failed"), e.Message);
        }
        finally
        {
            IsWorking = false;
        }
    }

    /// <summary>Asks to stop a running or starting server and waits for it. Returns false when the update should wait.</summary>
    private async Task<bool> StopServerForUpdateAsync()
    {
        if (_controller.Preparing is not null)
        {
            // Mid-backup or mid-Java-download: not safe to restart, and nothing to stop yet.
            StatusText = Text.Get("Update_WaitForStart");
            return false;
        }

        if (!_controller.HasRunningServer)
        {
            return true;
        }

        if (!_dialogs.Confirm(Text.Get("Update_StopServerConfirm")))
        {
            return false;
        }

        await _controller.ShutdownAsync();

        // False when the user chose not to force-close a server that didn't stop; it keeps running.
        return !_controller.HasRunningServer;
    }

    [RelayCommand]
    private void Later() => Dismissed = true;

    private async Task CheckAsync(bool quiet)
    {
        if (!IsInstalled)
        {
            StatusText = Text.Get("Update_NotInstalled");
            return;
        }

        IsWorking = true;
        StatusText = quiet ? null : Text.Get("Update_Checking");
        try
        {
            _update = await _updater.CheckAsync();
            AvailableVersion = _update?.TargetFullRelease.Version.ToString();
            _log.Write($"{(quiet ? "Startup" : "Manual")} check: {AvailableVersion ?? "up to date"} (running {_updater.CurrentVersion}).");
            OnPropertyChanged(nameof(CardText));
            Dismissed = false;
            StatusText = quiet ? null : _update is null ? Text.Get("Update_UpToDate") : CardText;
        }
        catch (Exception e)
        {
            // Offline, GitHub's rate limit or a damaged feed: never worth more than a line in App settings and the log.
            _log.Write($"{(quiet ? "Startup" : "Manual")} check failed: {e.GetType().Name}: {e.Message}");
            StatusText = quiet ? null : string.Format(Text.Get("Update_CheckFailed"), e.Message);
        }
        finally
        {
            IsWorking = false;
        }
    }
}
