using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WaNut.App.Resources;
using WaNut.Core.Hosting;
using WaNut.Core.Players;
using WaNut.Core.Properties;
using WaNut.Core.Settings;

namespace WaNut.App.ViewModels;

/// <summary>A name on the whitelist or OP list, with the text a screen reader says for its Remove button.</summary>
public sealed record PlayerEntry(string Name, string RemoveLabel);

/// <summary>
/// The whitelist and OP list on the Players tab. While the server is online, changes go to it as commands; while
/// it is stopped they are queued and sent the next time it comes online, because only the server can look up a
/// player's UUID for whitelist.json and ops.json.
/// </summary>
public sealed partial class PlayerListsViewModel : ObservableObject
{
    private const string WhitelistKey = "white-list";

    /// <summary>The server writes whitelist.json and ops.json once it has looked the player up.</summary>
    private static readonly TimeSpan RefreshDelay = TimeSpan.FromSeconds(3);

    private readonly ServerViewModel _server;
    private bool _loading;

    public PlayerListsViewModel(ServerViewModel server)
    {
        _server = server;
        Reload();
    }

    public ObservableCollection<PlayerEntry> Whitelist { get; } = [];

    public ObservableCollection<PlayerEntry> Operators { get; } = [];

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

    /// <summary>Reads both lists from the server's files, plus changes still waiting for the next start.</summary>
    public void Reload()
    {
        try
        {
            var dir = _server.InstanceDir;
            var pending = _server.Metadata.PendingCommands;
            Fill(Whitelist, PlayerLists.WithPending(PlayerLists.ReadNames(dir, PlayerList.Whitelist), pending, PlayerList.Whitelist));
            Fill(Operators, PlayerLists.WithPending(PlayerLists.ReadNames(dir, PlayerList.Operators), pending, PlayerList.Operators));

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

    private async Task ChangeAsync(PlayerList list, string name, bool add)
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

        var shown = list == PlayerList.Whitelist ? Whitelist : Operators;
        if (_server.IsOnline)
        {
            await _server.SendCommandAsync(add ? PlayerLists.AddCommand(list, name) : PlayerLists.RemoveCommand(list, name));
            Fill(shown, add
                ? shown.Select(e => e.Name).Append(name).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                : shown.Select(e => e.Name).Where(n => !n.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList());
            OnPropertyChanged(nameof(ShowEmptyWhitelistWarning));
            RefreshSoon();
            return;
        }

        try
        {
            var fileNames = PlayerLists.ReadNames(_server.InstanceDir, list);
            var pending = PlayerLists.Queue(_server.Metadata.PendingCommands, list, name, add, fileNames);
            _server.UpdateMetadata(_server.Metadata with { PendingCommands = pending });
        }
        catch (IOException e)
        {
            _server.ShowError(string.Format(Text.Get("Settings_SaveFailed"), e.Message));
        }

        Reload();
    }

    private static void Fill(ObservableCollection<PlayerEntry> target, IEnumerable<string> names)
    {
        target.Clear();
        foreach (var name in names)
        {
            target.Add(new PlayerEntry(name, string.Format(Text.Get("Players_RemoveName"), name)));
        }
    }
}
