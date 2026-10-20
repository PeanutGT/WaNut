using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WaNut.App.Resources;
using WaNut.App.Services;
using WaNut.Core.Hosting;
using WaNut.Core.Players;
using WaNut.Core.Properties;
using WaNut.Core.Settings;

namespace WaNut.App.ViewModels;

/// <summary>
/// The people on the Players tab: who is online, who joined before, and the whitelist, OP list and ban list. While
/// the server is online, list changes go to it as commands; while it is stopped they are queued and sent the next
/// time it comes online, because only the server can look up a player's UUID for whitelist.json, ops.json and
/// banned-players.json.
/// </summary>
public sealed partial class PlayerListsViewModel : ObservableObject
{
    private const string WhitelistKey = "white-list";

    /// <summary>The server writes whitelist.json and ops.json once it has looked the player up.</summary>
    private static readonly TimeSpan RefreshDelay = TimeSpan.FromSeconds(3);

    private readonly ServerViewModel _server;
    private readonly IDialogService _dialogs;
    private readonly PlayerFaces _faces;
    private bool _loading;

    public PlayerListsViewModel(ServerViewModel server, IDialogService dialogs, PlayerFaces faces)
    {
        _server = server;
        _dialogs = dialogs;
        _faces = faces;
        server.Players.CollectionChanged += (_, _) => FillPeople();
        Reload();
    }

    public ObservableCollection<PlayerEntry> Whitelist { get; } = [];

    public ObservableCollection<PlayerEntry> Operators { get; } = [];

    public ObservableCollection<PlayerEntry> Banned { get; } = [];

    public ObservableCollection<PlayerEntry> Online { get; } = [];

    /// <summary>Everyone the server remembers from usercache.json who is not online now.</summary>
    public ObservableCollection<PlayerEntry> Recent { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyWhitelistWarning))]
    private bool _whitelistOn;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddToWhitelistCommand))]
    private string _newWhitelistName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddOperatorCommand))]
    private string _newOperatorName = string.Empty;

    [ObservableProperty]
    private string? _nameError;

    /// <summary>A whitelist with nobody on it keeps everyone out, including the host.</summary>
    public bool ShowEmptyWhitelistWarning => WhitelistOn && Whitelist.Count == 0;

    public bool HasPendingChanges => _server.Metadata.PendingCommands.Count > 0 && !_server.IsOnline;

    /// <summary>The switch is a server.properties key while stopped and a command while online.</summary>
    public bool CanChangeWhitelistSwitch => _server.Status is ServerStatus.Stopped or ServerStatus.Online;

    /// <summary>Reads the lists from the server's files, plus changes still waiting for the next start.</summary>
    public void Reload()
    {
        try
        {
            var dir = _server.InstanceDir;
            var pending = _server.Metadata.PendingCommands;
            Fill(Whitelist, PlayerLists.WithPending(PlayerLists.ReadNames(dir, PlayerList.Whitelist), pending, PlayerList.Whitelist));
            Fill(Operators, PlayerLists.WithPending(PlayerLists.ReadNames(dir, PlayerList.Operators), pending, PlayerList.Operators));
            Fill(Banned, PlayerLists.WithPending(PlayerLists.ReadNames(dir, PlayerList.Banned), pending, PlayerList.Banned), "Players_UnbanName");
            FillPeople();

            _loading = true;
            WhitelistOn = PropertiesFile.Load(Path.Combine(dir, GameSettingsStore.PropertiesFileName)).Get(WhitelistKey)?.Trim() == "true";
        }
        catch (IOException e)
        {
            _server.ShowError(string.Format(Text.Get("Settings_LoadFailed"), e.Message));
        }
        finally
        {
            _loading = false;
        }

        OnPropertyChanged(nameof(ShowEmptyWhitelistWarning));
        OnPropertyChanged(nameof(HasPendingChanges));
    }

    /// <summary>Reads the files again once the server has had time to write them.</summary>
    public void RefreshSoon() => _ = ReloadAfterDelayAsync();

    private async Task ReloadAfterDelayAsync()
    {
        await Task.Delay(RefreshDelay);
        Reload();
    }

    internal void OnServerStatusChanged()
    {
        OnPropertyChanged(nameof(CanChangeWhitelistSwitch));
        OnPropertyChanged(nameof(HasPendingChanges));
        if (_server.Status == ServerStatus.Stopped)
        {
            Reload();
        }
    }

    partial void OnWhitelistOnChanged(bool value)
    {
        if (!_loading)
        {
            _ = SetWhitelistAsync(value);
        }
    }

    private async Task SetWhitelistAsync(bool on)
    {
        if (_server.IsOnline)
        {
            // The server saves the change to server.properties itself.
            await _server.SendCommandAsync(on ? "whitelist on" : "whitelist off");
            return;
        }

        try
        {
            var path = Path.Combine(_server.InstanceDir, GameSettingsStore.PropertiesFileName);
            var properties = PropertiesFile.Load(path);
            properties.Set(WhitelistKey, on ? "true" : "false");
            properties.Save(path);
        }
        catch (IOException e)
        {
            _server.ShowError(string.Format(Text.Get("Settings_SaveFailed"), e.Message));
        }
    }

    [RelayCommand(CanExecute = nameof(CanAddToWhitelist))]
    private Task AddToWhitelistAsync() => ChangeAsync(PlayerList.Whitelist, NewWhitelistName.Trim(), add: true);

    private bool CanAddToWhitelist() => NewWhitelistName.Trim().Length > 0;

    [RelayCommand(CanExecute = nameof(CanAddOperator))]
    private Task AddOperatorAsync() => ChangeAsync(PlayerList.Operators, NewOperatorName.Trim(), add: true);

    private bool CanAddOperator() => NewOperatorName.Trim().Length > 0;

    [RelayCommand]
    private Task RemoveFromWhitelistAsync(PlayerEntry entry) => ChangeAsync(PlayerList.Whitelist, entry.Name, add: false);

    [RelayCommand]
    private Task RemoveOperatorAsync(PlayerEntry entry) => ChangeAsync(PlayerList.Operators, entry.Name, add: false);

    [RelayCommand]
    private Task UnbanAsync(PlayerEntry entry) => ChangeAsync(PlayerList.Banned, entry.Name, add: false);

    [RelayCommand]
    private Task ToggleOperatorAsync(PlayerEntry entry) => ChangeAsync(PlayerList.Operators, entry.Name, add: !entry.IsOperator);

    [RelayCommand]
    private Task ToggleWhitelistAsync(PlayerEntry entry) => ChangeAsync(PlayerList.Whitelist, entry.Name, add: !entry.IsWhitelisted);

    [RelayCommand]
    private async Task KickAsync(PlayerEntry entry)
    {
        if (_server.IsOnline && PlayerLists.IsValidName(entry.Name))
        {
            await _server.SendCommandAsync(PlayerLists.KickCommand(entry.Name, null));
        }
    }

    [RelayCommand]
    private async Task ToggleBanAsync(PlayerEntry entry)
    {
        if (entry.IsBanned)
        {
            await ChangeAsync(PlayerList.Banned, entry.Name, add: false);
        }
        else if (_dialogs.AskBan(entry.Name) is { } reason)
        {
            await ChangeAsync(PlayerList.Banned, entry.Name, add: true, reason);
        }
    }

    private async Task ChangeAsync(PlayerList list, string name, bool add, string? reason = null)
    {
        // The name goes into a console command, so only real player names get through.
        if (!PlayerLists.IsValidName(name))
        {
            NameError = Text.Get("Players_InvalidName");
            return;
        }

        NameError = null;
        if (list == PlayerList.Whitelist)
        {
            NewWhitelistName = string.Empty;
        }
        else
        {
            NewOperatorName = string.Empty;
        }

        var shown = list switch
        {
            PlayerList.Whitelist => Whitelist,
            PlayerList.Operators => Operators,
            _ => Banned,
        };
        if (_server.IsOnline)
        {
            await _server.SendCommandAsync(!add ? PlayerLists.RemoveCommand(list, name)
                : list == PlayerList.Banned ? PlayerLists.BanCommand(name, reason) : PlayerLists.AddCommand(list, name));
            Fill(shown, add
                ? shown.Select(e => e.Name).Append(name).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                : shown.Select(e => e.Name).Where(n => !n.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList(),
                list == PlayerList.Banned ? "Players_UnbanName" : "Players_RemoveName");
            FillPeople();
            OnPropertyChanged(nameof(ShowEmptyWhitelistWarning));
            RefreshSoon();
            return;
        }

        try
        {
            var fileNames = PlayerLists.ReadNames(_server.InstanceDir, list);
            var pending = PlayerLists.Queue(_server.Metadata.PendingCommands, list, name, add, fileNames, reason);
            _server.UpdateMetadata(_server.Metadata with { PendingCommands = pending });
        }
        catch (IOException e)
        {
            _server.ShowError(string.Format(Text.Get("Settings_SaveFailed"), e.Message));
        }

        Reload();
    }

    private void Fill(ObservableCollection<PlayerEntry> target, IEnumerable<string> names, string removeLabelKey = "Players_RemoveName")
    {
        target.Clear();
        foreach (var name in names)
        {
            target.Add(Entry(name, string.Format(Text.Get(removeLabelKey), name)));
        }
    }

    /// <summary>Who is online and who joined before, each marked with the lists they are on, for the ⋯ menu.</summary>
    private void FillPeople()
    {
        var online = _server.Players.ToList();
        var recent = UserCache.Read(_server.InstanceDir)
            .Select(p => p.Name)
            .Where(n => !online.Contains(n, StringComparer.OrdinalIgnoreCase));
        FillPeople(Online, online, isOnline: true);
        FillPeople(Recent, recent, isOnline: false);
    }

    private void FillPeople(ObservableCollection<PlayerEntry> target, IEnumerable<string> names, bool isOnline)
    {
        target.Clear();
        foreach (var name in names)
        {
            var entry = Entry(name, string.Format(Text.Get("Players_MoreName"), name));
            entry.IsOnline = isOnline;
            entry.IsOperator = Operators.Any(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            entry.IsWhitelisted = Whitelist.Any(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            entry.IsBanned = Banned.Any(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            target.Add(entry);
        }
    }

    private PlayerEntry Entry(string name, string actionLabel)
    {
        var entry = new PlayerEntry(name, actionLabel);
        _ = LoadFaceAsync(entry);
        return entry;
    }

    private async Task LoadFaceAsync(PlayerEntry entry) => entry.Face = await _faces.GetAsync(entry.Name);
}
