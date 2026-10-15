using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using WaNut.Core.Backups;
using WaNut.Core.Settings;

namespace WaNut.App.ViewModels;

/// <summary>WaNut's own settings. One instance for the whole app, so every server tab sees a change at once.</summary>
public sealed partial class AppSettingsViewModel : ObservableObject
{
    private readonly AppSettingsStore _store;
    private AppSettings _saved;
    private bool _loading;

    public AppSettingsViewModel(AppSettingsStore store)
    {
        _store = store;
        var settings = store.Load();
        _saved = settings;
        _loading = true;
        AdvancedMode = settings.AdvancedMode;
        BackupKeepCount = settings.Backups.KeepCount;
        BackupMaxAgeDays = settings.Backups.MaxAgeDays;
        _loading = false;
    }

    [ObservableProperty]
    private bool _advancedMode;

    [ObservableProperty]
    private int _backupKeepCount;

    [ObservableProperty]
    private int _backupMaxAgeDays;

    [ObservableProperty]
    private string? _errorText;

    public BackupRules BackupRules => new() { KeepCount = BackupKeepCount, MaxAgeDays = BackupMaxAgeDays };

    public bool WasFirewallAsked(string javaExe) => _saved.FirewallAsked.Contains(javaExe, StringComparer.OrdinalIgnoreCase);

    public void MarkFirewallAsked(string javaExe)
    {
        if (!WasFirewallAsked(javaExe))
        {
            _saved = _saved with { FirewallAsked = [.. _saved.FirewallAsked, javaExe] };
            Save();
        }
    }

    partial void OnAdvancedModeChanged(bool value) => Save();

    partial void OnBackupKeepCountChanged(int oldValue, int newValue)
    {
        if (!_loading && !BackupRules.IsValidKeepCount(newValue))
        {
            // An out-of-range number snaps back instead of being saved.
            _loading = true;
            BackupKeepCount = oldValue;
            _loading = false;
            return;
        }

        Save();
    }

    partial void OnBackupMaxAgeDaysChanged(int oldValue, int newValue)
    {
        if (!_loading && !BackupRules.IsValidMaxAgeDays(newValue))
        {
            _loading = true;
            BackupMaxAgeDays = oldValue;
            _loading = false;
            return;
        }

        Save();
    }

    private void Save()
    {
        if (_loading)
        {
            return;
        }

        try
        {
            _saved = _saved with { AdvancedMode = AdvancedMode, Backups = BackupRules };
            _store.Save(_saved);
            ErrorText = null;
        }
        catch (IOException e)
        {
            ErrorText = string.Format(Resources.Text.Get("Settings_SaveFailed"), e.Message);
        }
    }
}
