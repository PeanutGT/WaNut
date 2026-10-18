using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WaNut.App.Services;
using WaNut.Core.Backups;
using WaNut.Core.Notifications;
using WaNut.Core.Settings;

namespace WaNut.App.ViewModels;

/// <summary>WaNut's own settings. One instance for the whole app, so every server tab sees a change at once.</summary>
public sealed partial class AppSettingsViewModel : ObservableObject
{
    private readonly AppSettingsStore _store;
    private readonly ThemeService _themeService;
    private readonly DiscordWebhook _webhook;
    private AppSettings _saved;
    private bool _loading;

    public AppSettingsViewModel(AppSettingsStore store, ThemeService theme, DiscordWebhook webhook)
    {
        _webhook = webhook;
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
        CloseAction = settings.CloseAction;
        BackupCopyDir = settings.BackupCopyDir ?? string.Empty;
        DiscordWebhookUrl = settings.Discord.WebhookUrl ?? string.Empty;
        DiscordOnline = settings.Discord.Online;
        DiscordStopped = settings.Discord.Stopped;
        DiscordCrashed = settings.Discord.Crashed;
        DiscordPlayerJoined = settings.Discord.PlayerJoined;
        DiscordPlayerLeft = settings.Discord.PlayerLeft;
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

    [ObservableProperty]
    private CloseAction _closeAction;

    public IReadOnlyList<KeyValuePair<CloseAction, string>> CloseActions { get; } =
    [
        new(Core.Settings.CloseAction.Ask, Resources.Text.Get("Close_ActionAsk")),
        new(Core.Settings.CloseAction.Tray, Resources.Text.Get("Close_ActionTray")),
        new(Core.Settings.CloseAction.Stop, Resources.Text.Get("Close_ActionStop")),
    ];

    partial void OnCloseActionChanged(CloseAction value) => Save();

    /// <summary>Where every backup is also copied; empty for nowhere.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBackupCopyDir))]
    private string _backupCopyDir = string.Empty;

    public bool HasBackupCopyDir => BackupCopyDir.Trim().Length > 0;

    partial void OnBackupCopyDirChanged(string value) => Save();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiscordUrlInvalid))]
    [NotifyCanExecuteChangedFor(nameof(SendDiscordTestCommand))]
    private string _discordWebhookUrl = string.Empty;

    [ObservableProperty]
    private bool _discordOnline;

    [ObservableProperty]
    private bool _discordStopped;

    [ObservableProperty]
    private bool _discordCrashed;

    [ObservableProperty]
    private bool _discordPlayerJoined;

    [ObservableProperty]
    private bool _discordPlayerLeft;

    [ObservableProperty]
    private string? _discordTestText;

    public bool DiscordUrlInvalid => DiscordWebhookUrl.Trim().Length > 0 && !DiscordWebhook.IsValidUrl(DiscordWebhookUrl);

    public DiscordSettings Discord => new()
    {
        WebhookUrl = DiscordWebhookUrl.Trim().Length == 0 ? null : DiscordWebhookUrl.Trim(),
        Online = DiscordOnline,
        Stopped = DiscordStopped,
        Crashed = DiscordCrashed,
        PlayerJoined = DiscordPlayerJoined,
        PlayerLeft = DiscordPlayerLeft,
    };

    partial void OnDiscordWebhookUrlChanged(string value) => Save();

    partial void OnDiscordOnlineChanged(bool value) => Save();

    partial void OnDiscordStoppedChanged(bool value) => Save();

    partial void OnDiscordCrashedChanged(bool value) => Save();

    partial void OnDiscordPlayerJoinedChanged(bool value) => Save();

    partial void OnDiscordPlayerLeftChanged(bool value) => Save();

    [RelayCommand(CanExecute = nameof(CanSendDiscordTest))]
    private async Task SendDiscordTestAsync()
    {
        DiscordTestText = Resources.Text.Get("Discord_Sending");
        try
        {
            await _webhook.SendAsync(DiscordWebhookUrl, Resources.Text.Get("Discord_TestMessage"));
            DiscordTestText = Resources.Text.Get("Discord_TestSent");
        }
        catch (Exception e) when (e is System.Net.Http.HttpRequestException or TaskCanceledException or ArgumentException)
        {
            DiscordTestText = string.Format(Resources.Text.Get("Discord_TestFailed"), e.Message);
        }
    }

    private bool CanSendDiscordTest() => DiscordWebhook.IsValidUrl(DiscordWebhookUrl);

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

    public bool GuideIntroDone => _saved.GuideIntroDone;

    public bool GuideTabsDone => _saved.GuideTabsDone;

    /// <summary>Raised by "Show the guide again"; the main window runs the whole tour from the start.</summary>
    public event EventHandler? GuideRequested;

    public void RequestGuide() => GuideRequested?.Invoke(this, EventArgs.Empty);

    public void SaveGuideProgress(bool introDone, bool tabsDone)
    {
        _saved = _saved with { GuideIntroDone = introDone, GuideTabsDone = tabsDone };
        Save();
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
            _saved = _saved with
            {
                AdvancedMode = AdvancedMode,
                Backups = BackupRules,
                CheckForUpdates = CheckForUpdates,
                Language = Language,
                Theme = Theme,
                InviteNote = InviteNote.Trim().Length == 0 ? null : InviteNote.Trim(),
                Discord = Discord,
                CloseAction = CloseAction,
                BackupCopyDir = HasBackupCopyDir ? BackupCopyDir.Trim() : null,
            };
            _store.Save(_saved);
            ErrorText = null;
        }
        catch (IOException e)
        {
            ErrorText = string.Format(Resources.Text.Get("Settings_SaveFailed"), e.Message);
        }
    }
}
