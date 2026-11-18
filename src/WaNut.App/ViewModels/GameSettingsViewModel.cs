using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using WaNut.App.Resources;
using WaNut.Core.Hosting;
using WaNut.Core.Properties;
using WaNut.Core.Settings;
using WaNut.Core.Settings.Config;

namespace WaNut.App.ViewModels;

/// <summary>A choice in a drop-down: the value and the text shown for it.</summary>
public sealed record Option<T>(T Value, string Label)
{
    // The closed box and screen readers show the item's text, not the record's.
    public override string ToString() => Label;
}

/// <summary>
/// The Game settings tab, built from <see cref="ServerConfig"/>: the core settings always open, two collapsible
/// groups, a search box, and (in Advanced mode) the full list of server.properties lines. All of them edit the same
/// <see cref="ConfigItem"/> objects. Every change is saved right away. server.properties is only read by the server
/// when it starts (and rewritten by it while running), so those settings are locked while it runs; game rules can
/// change at any time and are sent to a running server at once.
/// </summary>
public sealed partial class GameSettingsViewModel : ObservableObject
{
    private readonly ServerViewModel _server;
    private readonly List<SettingEntryViewModel> _entries = [];
    private ServerConfig? _config;
    private bool _loading;
    private bool _worldExists;
    private bool _wasSearching;

    public GameSettingsViewModel(ServerViewModel server, AppSettingsViewModel app)
    {
        _server = server;
        App = app;
        WorldRules = new SettingsGroup(Text.Get("Settings_GroupWorldRules"));
        NetworkPerf = new SettingsGroup(Text.Get("Settings_GroupNetworkPerf"));
        FullList = new SettingsGroup(Text.Get("Settings_FullProperties"));
        PropertyChangedEventManager.AddHandler(app, (_, _) => OnPropertyChanged(nameof(ShowNoResults)), nameof(AppSettingsViewModel.AdvancedMode));
        Reload();
    }

    /// <summary>For the Advanced mode switch: the full property list shows only with it on.</summary>
    public AppSettingsViewModel App { get; }

    /// <summary>Always open.</summary>
    public ObservableCollection<SettingEntryViewModel> Core { get; } = [];

    public SettingsGroup WorldRules { get; }

    public SettingsGroup NetworkPerf { get; }

    /// <summary>Every server.properties line, the catalog's and the ones a mod added (Advanced mode).</summary>
    public SettingsGroup FullList { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoResults))]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoResults))]
    private bool _coreHasMatch = true;

    /// <summary>A search is on and nothing the user can see matches it.</summary>
    public bool ShowNoResults =>
        SearchText.Trim().Length > 0
        && !CoreHasMatch
        && !WorldRules.IsShown
        && !NetworkPerf.IsShown
        && !(App.AdvancedMode && FullList.IsShown);

    /// <summary>server.properties settings can only change while the server is stopped.</summary>
    public bool CanEditProperties => _server.Status == ServerStatus.Stopped;

    public bool ShowStopHint => !CanEditProperties;

    /// <summary>The seed only matters before the world is generated.</summary>
    private bool CanEditSeed => CanEditProperties && !_worldExists && !_server.FilesOnVps;

    private bool ShowSeedLocked => _worldExists || _server.FilesOnVps;

    /// <summary>Reads the settings again, e.g. after the server stopped and may have rewritten server.properties.</summary>
    public void Reload()
    {
        _loading = true;
        try
        {
            var file = PropertiesFile.Load(Path.Combine(_server.InstanceDir, GameSettingsStore.PropertiesFileName));
            _worldExists = GameSettingsStore.WorldExists(_server.InstanceDir);
            Build(ServerConfig.Load(file, _server.Metadata.MinecraftVersion, _server.Metadata.GameRules));
        }
        catch (IOException e)
        {
            _server.ShowError(string.Format(Text.Get("Settings_LoadFailed"), e.Message));
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>Says where changes go while the files are on the VPS; null otherwise.</summary>
    public bool ShowVpsSettingsNote => _server.ConfigOnVps;

    public string VpsSettingsNote => Text.Get(_server.VpsSettingsReady ? "Game_VpsSettingsNote" : "Game_VpsSettingsLoading");

    internal void OnVpsStateChanged()
    {
        OnPropertyChanged(nameof(ShowVpsSettingsNote));
        OnPropertyChanged(nameof(VpsSettingsNote));
        RefreshLocks();
    }

    /// <summary>The tab was shown: with the files on the VPS this copies its settings file first.</summary>
    public void OnShown()
    {
        // The Server tab may have changed the port or account check since this tab was built.
        if (_server.Status == ServerStatus.Stopped)
        {
            Reload();
        }

        _ = _server.EnsureVpsSettingsAsync();
    }

    internal void OnServerStatusChanged()
    {
        OnPropertyChanged(nameof(CanEditProperties));
        OnPropertyChanged(nameof(ShowStopHint));
        RefreshLocks();
        if (_server.Status == ServerStatus.Stopped)
        {
            Reload();
        }
    }

    partial void OnSearchTextChanged(string value) => ApplySearch();

    private void Build(ServerConfig config)
    {
        foreach (var entry in _entries)
        {
            entry.Item.PropertyChanged -= OnItemChanged;
        }

        _config = config;
        _entries.Clear();
        Core.Clear();
        WorldRules.Entries.Clear();
        NetworkPerf.Entries.Clear();
        FullList.Entries.Clear();
        var made = new Dictionary<ConfigItem, SettingEntryViewModel>();
        foreach (var item in config.Items)
        {
            var entry = new SettingEntryViewModel(item);
            made[item] = entry;
            _entries.Add(entry);
            item.PropertyChanged += OnItemChanged;
            var list = item.Meta.Section switch
            {
                ConfigSection.Core => Core,
                ConfigSection.WorldRules => WorldRules.Entries,
                ConfigSection.NetworkPerf => NetworkPerf.Entries,
                _ => null,
            };
            list?.Add(entry);
        }

        foreach (var item in config.FileItems)
        {
            FullList.Entries.Add(made[item]);
        }

        RefreshLocks();
        ApplySearch();
    }

    private void RefreshLocks()
    {
        foreach (var entry in _entries)
        {
            var meta = entry.Item.Meta;
            entry.IsEnabled = meta.IsGameRuleItem || (meta.Key == "level-seed" ? CanEditSeed : CanEditProperties);
            entry.Note = meta.Key == "level-seed" && ShowSeedLocked ? Text.Get("Game_SeedLocked") : string.Empty;
        }
    }

    /// <summary>
    /// Hides what does not match and opens the groups that have a match. What the user had open is put back when
    /// the search is cleared.
    /// </summary>
    private void ApplySearch()
    {
        var query = SearchText.Trim();
        var searching = query.Length > 0;
        foreach (var entry in _entries)
        {
            entry.IsVisible = !searching || entry.Matches(query);
        }

        foreach (var group in new[] { WorldRules, NetworkPerf, FullList })
        {
            var hasMatch = group.Entries.Any(e => e.IsVisible);
            group.IsShown = !searching || hasMatch;
            if (searching && !_wasSearching)
            {
                group.ExpandedBeforeSearch = group.IsExpanded;
            }

            if (searching)
            {
                group.IsExpanded = hasMatch;
            }
            else if (_wasSearching)
            {
                group.IsExpanded = group.ExpandedBeforeSearch;
            }
        }

        _wasSearching = searching;
        CoreHasMatch = !searching || Core.Any(e => e.IsVisible);
        OnPropertyChanged(nameof(ShowNoResults));
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_loading && e.PropertyName == nameof(ConfigItem.RawValue))
        {
            _ = SaveAsync();
        }
    }

    private async Task SaveAsync()
    {
        if (_config is not { } config)
        {
            return;
        }

        IReadOnlyList<string> commands;
        try
        {
            var path = Path.Combine(_server.InstanceDir, GameSettingsStore.PropertiesFileName);
            var file = PropertiesFile.Load(path);
            if (config.ApplyTo(file))
            {
                file.Save(path);
            }

            var updated = config.ApplyRulesTo(_server.Metadata, out commands);
            if (!ReferenceEquals(updated, _server.Metadata))
            {
                _server.UpdateMetadata(updated);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _server.ShowError(string.Format(Text.Get("Settings_SaveFailed"), e.Message));
            return;
        }

        if (_server.IsOnline)
        {
            foreach (var command in commands)
            {
                await _server.SendCommandAsync(command);
            }
        }

        // Last, so a failed send (which puts the tab back to what the VPS has) isn't overwritten by the lines above.
        await _server.PushVpsSettingsAsync();
    }
}
