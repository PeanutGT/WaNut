using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WaNut.App.Resources;
using WaNut.App.Services;
using WaNut.Core.Bridge;

namespace WaNut.App.ViewModels;

/// <summary>
/// The Discord tab: links the server's chat with a Discord channel. A small background program (the bridge) does the
/// work and keeps running when WaNut closes; this tab sets it up, shows how it is doing and removes it.
/// </summary>
public sealed partial class DiscordViewModel : ObservableObject
{
    private static readonly Uri DeveloperPortal = new("https://discord.com/developers/applications");

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly ServerViewModel _server;
    private readonly BridgeManager _manager;
    private readonly AppSettingsViewModel _app;
    private readonly IDialogService _dialogs;
    private CancellationTokenSource? _polling;
    private string _savedToken;
    private string _savedWebhook;

    public DiscordViewModel(ServerViewModel server, BridgeManager manager, AppSettingsViewModel app, IDialogService dialogs)
    {
        _server = server;
        _manager = manager;
        _app = app;
        _dialogs = dialogs;

        var saved = manager.Load(server.Id);
        var config = saved.Config;
        _savedToken = config.BotToken ?? string.Empty;
        _savedWebhook = config.WebhookUrl ?? string.Empty;
        _channelId = config.ChannelId ?? string.Empty;
        _adminRoleId = config.AdminRoleId ?? string.Empty;
        _presentation = PresentationOptions.First(o => o.Value == config.Presentation);
        _language = LanguageOptions.FirstOrDefault(o => o.Code == config.Language) ?? LanguageOptions[0];
        _startOnBoot = saved.StartOnBoot;
        _chat = config.Filters.Chat;
        _joinLeave = config.Filters.JoinLeave;
        _deathsAndAdvancements = config.Filters.DeathsAndAdvancements;
        _serverState = config.Filters.ServerState;
        _onlineCountStatus = config.Filters.OnlineCountStatus;
        _listCommand = config.Filters.ListCommand;
        _remoteConsole = config.Filters.RemoteConsole;
        _ignorePrefixes = config.Filters.IgnoredPrefixes.Count > 0;
        _ignoredPrefixes = string.Join(' ', config.Filters.IgnoredPrefixes);
        if (string.IsNullOrEmpty(_savedToken) && string.IsNullOrEmpty(_savedWebhook) && _app.Language == "zh-TW")
        {
            // A fresh form follows the app's language until the user picks one.
            _language = LanguageOptions.First(o => o.Code == "zh-TW");
        }
    }

    public IReadOnlyList<PresentationOption> PresentationOptions { get; } =
    [
        new(DiscordPresentation.Webhook, Text.Get("Discord_ModeWebhook")),
        new(DiscordPresentation.Embed, Text.Get("Discord_ModeEmbed")),
        new(DiscordPresentation.PlainText, Text.Get("Discord_ModePlain")),
    ];

    public IReadOnlyList<LanguageOption> LanguageOptions { get; } =
    [
        new("en", "English"),
        new("zh-TW", "繁體中文"),
    ];

    /// <summary>Where the bridge will run: the same place as the server, because it reads that server's log.</summary>
    public string LocationText => _server.Metadata.VpsId is { } id
        ? string.Format(CultureInfo.CurrentCulture, Text.Get("Discord_LocationVps"), _app.VpsList.FirstOrDefault(v => v.Id == id)?.Name ?? id)
        : Text.Get("Discord_LocationLocal");

    public bool RunsOnVps => _server.Metadata.VpsId is not null;

    public bool ShowsStartOnBoot => !RunsOnVps;

    /// <summary>The newest files are on the VPS and the server runs there: RCON can only be turned on while it is stopped.</summary>
    public bool ShowFilesOnVpsNote => _server.FilesOnVps && _server.IsRunning;

    // ----- The form. Tokens never show again once saved; the boxes start empty and an empty box keeps the saved one. -----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TokenHint))]
    private string _botToken = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WebhookHint))]
    private string _webhookUrl = string.Empty;

    [ObservableProperty]
    private string _channelId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsesWebhook))]
    private PresentationOption _presentation;

    [ObservableProperty]
    private LanguageOption _language;

    [ObservableProperty]
    private bool _startOnBoot;

    [ObservableProperty]
    private bool _chat;

    [ObservableProperty]
    private bool _joinLeave;

    [ObservableProperty]
    private bool _deathsAndAdvancements;

    [ObservableProperty]
    private bool _serverState;

    [ObservableProperty]
    private bool _onlineCountStatus;

    [ObservableProperty]
    private bool _listCommand;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAdminRole))]
    private bool _remoteConsole;

    [ObservableProperty]
    private string _adminRoleId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPrefixes))]
    private bool _ignorePrefixes;

    [ObservableProperty]
    private string _ignoredPrefixes;

    public bool UsesWebhook => Presentation.Value == DiscordPresentation.Webhook;

    public bool ShowAdminRole => RemoteConsole;

    public bool ShowPrefixes => IgnorePrefixes;

    public string? TokenHint => BotToken.Length == 0 && _savedToken.Length > 0 ? Text.Get("Discord_TokenSaved") : null;

    public string? WebhookHint => WebhookUrl.Length == 0 && _savedWebhook.Length > 0 ? Text.Get("Discord_WebhookSaved") : null;

    // ----- What happened. -----

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand), nameof(TestCommand), nameof(StopCommand), nameof(RemoveCommand))]
    private bool _isBusy;

    /// <summary>The deployment step in plain words while it runs.</summary>
    [ObservableProperty]
    private string? _stepText;

    [ObservableProperty]
    private string? _errorText;

    [ObservableProperty]
    private string? _okText;

    /// <summary>The bridge's last log lines when it failed to start; shown below the error.</summary>
    [ObservableProperty]
    private string? _errorDetail;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWarnings))]
    private IReadOnlyList<string> _warnings = [];

    /// <summary>RCON was just turned on in server.properties, so Minecraft must restart once before chat can come back from Discord.</summary>
    [ObservableProperty]
    private bool _restartNeeded;

    public bool HasWarnings => Warnings.Count > 0;

    // ----- Status board. -----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInstalled), nameof(CanStop), nameof(ApplyText))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand), nameof(RemoveCommand))]
    private BridgeStatus _status = BridgeStatus.NotInstalled;

    public ObservableCollection<string> LogLines { get; } = [];

    public bool IsInstalled => Status.State != BridgeState.NotInstalled;

    public bool CanStop => Status.State is BridgeState.Running or BridgeState.Starting;

    public string ApplyText => Text.Get(IsInstalled ? "Discord_Reapply" : "Discord_Apply");

    public string StateText => Status.State switch
    {
        BridgeState.NotInstalled => Text.Get("Discord_State_NotInstalled"),
        BridgeState.Stopped => Text.Get("Discord_State_Stopped"),
        BridgeState.Starting => Text.Get("Discord_State_Starting"),
        BridgeState.Running => Text.Get("Discord_State_Running"),
        BridgeState.Failed => Text.Get("Discord_State_Failed"),
        _ => Text.Get("Discord_State_Unknown"),
    };

    public string RconText => Text.Get(Status.RconConnected ? "Discord_RconOn" : "Discord_RconOff");

    public string DiscordText => !Status.DiscordConnected
        ? Text.Get("Discord_DiscordOff")
        : Status.DiscordLatencyMs is { } ms
            ? string.Format(CultureInfo.CurrentCulture, Text.Get("Discord_DiscordOnMs"), ms)
            : Text.Get("Discord_DiscordOn");

    public string? StatusMessage => Status.State is BridgeState.Failed or BridgeState.Unknown or BridgeState.Stopped ? Status.Message : null;

    public bool ShowRestartNote => RestartNeeded || Status.ServerRestartNeeded;

    partial void OnStatusChanged(BridgeStatus value)
    {
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(RconText));
        OnPropertyChanged(nameof(DiscordText));
        OnPropertyChanged(nameof(StatusMessage));
        OnPropertyChanged(nameof(ShowRestartNote));
    }

    partial void OnRestartNeededChanged(bool value) => OnPropertyChanged(nameof(ShowRestartNote));

    partial void OnPresentationChanged(PresentationOption value) => OnPropertyChanged(nameof(UsesWebhook));

    /// <summary>The server's state changed; the VPS note depends on whether it runs.</summary>
    internal void OnServerStatusChanged() => OnPropertyChanged(nameof(ShowFilesOnVpsNote));

    /// <summary>Called by the view's paste handlers: the password boxes don't bind, so the typed text goes straight here.</summary>
    internal void SetSecrets(string? token, string? webhook)
    {
        if (token is not null)
        {
            BotToken = DiscordTokenFormat.Normalize(token);
        }

        if (webhook is not null)
        {
            WebhookUrl = webhook.Trim();
        }
    }

    // ----- Polling while the tab is on screen. -----

    /// <summary>Starts asking the bridge how it is doing every few seconds; call from the UI thread.</summary>
    public void StartPolling()
    {
        if (_polling is not null)
        {
            return;
        }

        _polling = new CancellationTokenSource();
        _ = PollAsync(_polling.Token);
    }

    public void StopPolling()
    {
        _polling?.Cancel();
        _polling?.Dispose();
        _polling = null;
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        do
        {
            await RefreshCoreAsync(cancellationToken);
        }
        while (await WaitAsync(timer, cancellationToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    [RelayCommand]
    private Task RefreshAsync() => RefreshCoreAsync(CancellationToken.None);

    private async Task RefreshCoreAsync(CancellationToken cancellationToken)
    {
        // A deployment in progress reports for itself; asking in between would show a half-installed bridge.
        if (IsBusy)
        {
            return;
        }

        try
        {
            var status = await _manager.StatusAsync(_server.Metadata, cancellationToken);
            var log = status.State == BridgeState.NotInstalled ? [] : await _manager.LogAsync(_server.Metadata, 40, cancellationToken);
            if (IsBusy || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            Status = status;
            if (!LogLines.SequenceEqual(log))
            {
                LogLines.Clear();
                foreach (var line in log)
                {
                    LogLines.Add(line);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The tab was left.
        }
    }

    // ----- Commands. -----

    private bool CanAct() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task TestAsync()
    {
        ClearResult();
        if (Validate(requireChannel: true) is { } problem)
        {
            ErrorText = problem;
            return;
        }

        IsBusy = true;
        StepText = Text.Get("Discord_Step_Validate");
        try
        {
            var warnings = await _manager.TestAsync(BuildConfig());
            OkText = Text.Get("Discord_TestOk");
            Warnings = [.. warnings.Select(WarningText)];
        }
        catch (BridgeDeployException ex)
        {
            ShowError(ex);
        }
        finally
        {
            IsBusy = false;
            StepText = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task ApplyAsync()
    {
        ClearResult();
        if (Validate(requireChannel: true) is { } problem)
        {
            ErrorText = problem;
            return;
        }

        IsBusy = true;
        try
        {
            var progress = new Progress<BridgeDeployStep>(step => StepText = Text.Get("Discord_Step_" + step));
            var result = await _manager.DeployAsync(_server.Metadata, new BridgeSettings { StartOnBoot = StartOnBoot && !RunsOnVps, Config = BuildConfig() }, progress);
            Remember();
            Status = result.Status;
            RestartNeeded = result.ServerRestartNeeded;
            Warnings = [.. result.Warnings.Where(w => !(w == BridgeDeployWarning.NoStartOnBoot && RunsOnVps)).Select(WarningText)];
            OkText = Text.Get("Discord_DeployOk");
        }
        catch (BridgeDeployException ex)
        {
            ShowError(ex);
        }
        finally
        {
            IsBusy = false;
            StepText = null;
        }

        await RefreshCoreAsync(CancellationToken.None);
    }

    private bool CanStopNow() => !IsBusy && CanStop;

    [RelayCommand(CanExecute = nameof(CanStopNow))]
    private async Task StopAsync()
    {
        ClearResult();
        await RunAsync(() => _manager.StopAsync(_server.Metadata), "Discord_StoppedOk");
    }

    private bool CanRemoveNow() => !IsBusy && IsInstalled;

    [RelayCommand(CanExecute = nameof(CanRemoveNow))]
    private async Task RemoveAsync()
    {
        if (!_dialogs.Confirm(Text.Get("Discord_RemoveConfirm")))
        {
            return;
        }

        ClearResult();
        RestartNeeded = false;
        await RunAsync(() => _manager.RemoveAsync(_server.Metadata), "Discord_RemovedOk");
    }

    private async Task RunAsync(Func<Task> action, string doneKey)
    {
        IsBusy = true;
        try
        {
            await action();
            OkText = Text.Get(doneKey);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or Win32Exception or Renci.SshNet.Common.SshException or WaNut.Core.Remote.VpsUnreachableException or WaNut.Core.Remote.VpsLoginException or WaNut.Core.Remote.HostKeyException)
        {
            ErrorText = string.Format(CultureInfo.CurrentCulture, Text.Get("Discord_ActionFailed"), ex.Message);
        }
        finally
        {
            IsBusy = false;
        }

        await RefreshCoreAsync(CancellationToken.None);
    }

    [RelayCommand]
    private void OpenDeveloperPortal()
    {
        try
        {
            Process.Start(new ProcessStartInfo(DeveloperPortal.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Win32Exception ex)
        {
            ErrorText = string.Format(CultureInfo.CurrentCulture, Text.Get("Discord_OpenFailed"), ex.Message);
        }
    }

    // ----- Helpers. -----

    private void ClearResult()
    {
        ErrorText = null;
        ErrorDetail = null;
        OkText = null;
        Warnings = [];
    }

    /// <summary>A short plain message for what is missing or malformed, or null when the form can go to Discord.</summary>
    private string? Validate(bool requireChannel)
    {
        var token = EffectiveToken;
        var webhook = EffectiveWebhook;
        if (UsesWebhook ? webhook.Length == 0 : token.Length == 0)
        {
            return Text.Get(UsesWebhook ? "Discord_NeedWebhook" : "Discord_NeedToken");
        }

        if ((token.Length > 0 || requireChannel && !UsesWebhook) && !ulong.TryParse(ChannelId.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            return Text.Get("Discord_NeedChannel");
        }

        if (RemoteConsole && (token.Length == 0 || !ulong.TryParse(AdminRoleId.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out _)))
        {
            return Text.Get("Discord_NeedRole");
        }

        return null;
    }

    private string EffectiveToken => BotToken.Length > 0 ? BotToken : _savedToken;

    private string EffectiveWebhook => WebhookUrl.Length > 0 ? WebhookUrl : _savedWebhook;

    private BridgeConfig BuildConfig() => new()
    {
        ServerId = _server.Id,
        BotToken = NullIfEmpty(EffectiveToken),
        WebhookUrl = NullIfEmpty(EffectiveWebhook),
        ChannelId = NullIfEmpty(ChannelId.Trim()),
        Presentation = Presentation.Value,
        Language = Language.Code,
        AdminRoleId = RemoteConsole ? NullIfEmpty(AdminRoleId.Trim()) : null,
        Filters = new BridgeFilters
        {
            Chat = Chat,
            JoinLeave = JoinLeave,
            DeathsAndAdvancements = DeathsAndAdvancements,
            ServerState = ServerState,
            OnlineCountStatus = OnlineCountStatus,
            ListCommand = ListCommand,
            RemoteConsole = RemoteConsole,
            IgnoredPrefixes = IgnorePrefixes
                ? [.. IgnoredPrefixes.Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct()]
                : [],
        },
    };

    /// <summary>After a successful deployment the typed secrets are the saved ones; the boxes empty again.</summary>
    private void Remember()
    {
        _savedToken = EffectiveToken;
        _savedWebhook = EffectiveWebhook;
        BotToken = string.Empty;
        WebhookUrl = string.Empty;
        SecretsCleared?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The view empties its password boxes.</summary>
    public event EventHandler? SecretsCleared;

    private void ShowError(BridgeDeployException ex)
    {
        ErrorText = Text.Get("Discord_Error_" + ex.Error);
        var detail = ex.Detail?.Trim();
        ErrorDetail = string.IsNullOrEmpty(detail) ? null : detail;
    }

    private static string WarningText(BridgeDeployWarning warning) => Text.Get("Discord_Warning_" + warning);

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    /// <summary>A choice in the display-mode list.</summary>
    public sealed record PresentationOption(DiscordPresentation Value, string Name);

    /// <summary>A choice in the language list.</summary>
    public sealed record LanguageOption(string Code, string Name);
}
