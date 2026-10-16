using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using WaNut.App.Services;
using WaNut.Core.Backups;
using WaNut.Core.Settings;

namespace WaNut.App.ViewModels;

/// <summary>WaNut's own settings. One instance for the whole app, so every server tab sees a change at once.</summary>
public sealed partial class AppSettingsViewModel : ObservableObject
{
    private readonly AppSettingsStore _store;
    private readonly ThemeService _themeService;
    private AppSettings _saved;
    private bool _loading;

    public AppSettingsViewModel(AppSettingsStore store, ThemeService theme)
    {
        _store = store;
        _themeService = theme;
        var settings = store.Load();
        _saved = settings;
        _loading = true;
        AdvancedMode = settings.AdvancedMode;
        BackupKeepCount = settings.Backups.KeepCount;
        BackupMaxAgeDays = settings.Backups.MaxAgeDays;
        CheckForUpdates = settings.CheckForUpdates;
        Language = UiLanguages.Resolve(settings.Language);
        Theme = settings.Theme;
        InviteNote = settings.InviteNote ?? string.Empty;
        _loading = false;
    }

    [ObservableProperty]
    private bool _advancedMode;

    [ObservableProperty]
    private int _backupKeepCount;

    [ObservableProperty]
    private int _backupMaxAgeDays;

    [ObservableProperty]
    private bool _checkForUpdates;

    [ObservableProperty]
    private string? _errorText;

    /// <summary>A culture name from <see cref="UiLanguages"/>. Applied the next time WaNut starts.</summary>
    [ObservableProperty]
    private string _language = UiLanguages.English;

    /// <summary>Each language under its own name, so someone who can't read the current one can still find theirs.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Languages { get; } =
    [
        new(UiLanguages.English, Resources.Text.Get("Language_English")),
        new(UiLanguages.TraditionalChinese, Resources.Text.Get("Language_ZhTw")),
    ];

    [ObservableProperty]
    private ThemeChoice _theme;

    /// <summary>Remembered for every server's invite card: hosts usually use one VPN network.</summary>
    [ObservableProperty]
    private string _inviteNote = string.Empty;

    partial void OnInviteNoteChanged(string value) => Save();

    public IReadOnlyList<KeyValuePair<ThemeChoice, string>> Themes { get; } =
    [
        new(ThemeChoice.System, Resources.Text.Get("AppSettings_ThemeSystem")),
        new(ThemeChoice.Light, Resources.Text.Get("AppSettings_ThemeLight")),
        new(ThemeChoice.Dark, Resources.Text.Get("AppSettings_ThemeDark")),
    ];

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

    partial void OnCheckForUpdatesChanged(bool value) => Save();

    partial void OnLanguageChanged(string value) => Save();

    partial void OnThemeChanged(ThemeChoice value)
    {
        if (!_loading)
        {
            _themeService.Choice = value;
        }

        Save();
    }

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
            _saved = _saved with { AdvancedMode = AdvancedMode, Backups = BackupRules, CheckForUpdates = CheckForUpdates, Language = Language, Theme = Theme,
                InviteNote = InviteNote.Trim().Length == 0 ? null : InviteNote.Trim() };
            _store.Save(_saved);
            ErrorText = null;
        }
        catch (IOException e)
        {
            ErrorText = string.Format(Resources.Text.Get("Settings_SaveFailed"), e.Message);
        }
    }
}
