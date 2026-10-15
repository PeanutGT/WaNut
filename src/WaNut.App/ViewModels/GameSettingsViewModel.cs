using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using WaNut.App.Resources;
using WaNut.Core.Hosting;
using WaNut.Core.Settings;

namespace WaNut.App.ViewModels;

/// <summary>A choice in a drop-down: the value and the text shown for it.</summary>
public sealed record Option<T>(T Value, string Label)
{
    // The closed box and screen readers show the item's text, not the record's.
    public override string ToString() => Label;
}

/// <summary>
/// The Game settings tab. Every change is saved right away. server.properties is only read by the server when it
/// starts (and rewritten by it while running), so those settings are locked while it runs; game rules can change
/// at any time and are sent to a running server at once.
/// </summary>
public sealed partial class GameSettingsViewModel : ObservableObject
{
    // Minecraft's own limits.
    public const int MinDistance = 3;
    public const int MaxDistance = 32;

    private static readonly HashSet<string> SettingNames =
    [
        nameof(GameMode), nameof(Difficulty), nameof(Pvp), nameof(MaxPlayers), nameof(Motd), nameof(KeepInventory),
        nameof(ViewDistance), nameof(SimulationDistance), nameof(SpawnProtection), nameof(CommandBlocks),
        nameof(AllowFlight), nameof(SpawnMonsters), nameof(Hardcore), nameof(Seed),
    ];

    private readonly ServerViewModel _server;
    private readonly GameFeatures _features;
    private GameSettings _loaded = new();
    private bool _loading;
    private bool _worldExists;

    public GameSettingsViewModel(ServerViewModel server)
    {
        _server = server;
        _features = GameFeatures.For(server.Metadata.MinecraftVersion);
        Reload();
    }

    public IReadOnlyList<Option<GameMode>> GameModes { get; } =
        Enum.GetValues<GameMode>().Select(m => new Option<GameMode>(m, Text.Get($"Game_Mode{m}"))).ToList();

    public IReadOnlyList<Option<Difficulty>> Difficulties { get; } =
        Enum.GetValues<Difficulty>().Select(d => new Option<Difficulty>(d, Text.Get($"Game_Difficulty{d}"))).ToList();

    [ObservableProperty]
    private GameMode _gameMode;

    [ObservableProperty]
    private Difficulty _difficulty;

    [ObservableProperty]
    private bool _pvp;

    [ObservableProperty]
    private int _maxPlayers;

    [ObservableProperty]
    private string _motd = string.Empty;

    [ObservableProperty]
    private bool _keepInventory;

    [ObservableProperty]
    private int _viewDistance;

    [ObservableProperty]
    private int _simulationDistance;

    [ObservableProperty]
    private int _spawnProtection;

    [ObservableProperty]
    private bool _commandBlocks;

    [ObservableProperty]
    private bool _allowFlight;

    [ObservableProperty]
    private bool _spawnMonsters;

    [ObservableProperty]
    private bool _hardcore;

    [ObservableProperty]
    private string _seed = string.Empty;

    public bool HasSimulationDistance => _features.HasSimulationDistance;

    /// <summary>server.properties settings can only change while the server is stopped.</summary>
    public bool CanEditProperties => _server.Status == ServerStatus.Stopped;

    public bool ShowStopHint => !CanEditProperties;

    public bool CanEditPvp => CanEditProperties || _features.IsGameRule(RuleSetting.Pvp);

    public bool CanEditCommandBlocks => CanEditProperties || _features.IsGameRule(RuleSetting.CommandBlocks);

    public bool CanEditSpawnMonsters => CanEditProperties || _features.IsGameRule(RuleSetting.SpawnMonsters);

    /// <summary>The seed only matters before the world is generated.</summary>
    public bool CanEditSeed => CanEditProperties && !_worldExists;

    public bool ShowSeedLocked => _worldExists;

    /// <summary>Reads the settings again, e.g. after the server stopped and may have rewritten server.properties.</summary>
    public void Reload()
    {
        _loading = true;
        try
        {
            _loaded = GameSettingsStore.Load(_server.InstanceDir, _server.Metadata);
            _worldExists = GameSettingsStore.WorldExists(_server.InstanceDir);
            Show(_loaded);
        }
        catch (IOException e)
        {
            _server.ShowError(string.Format(Text.Get("Settings_LoadFailed"), e.Message));
        }
        finally
        {
            _loading = false;
        }

        OnPropertyChanged(nameof(CanEditSeed));
        OnPropertyChanged(nameof(ShowSeedLocked));
    }

    internal void OnServerStatusChanged()
    {
        OnPropertyChanged(nameof(CanEditProperties));
        OnPropertyChanged(nameof(ShowStopHint));
        OnPropertyChanged(nameof(CanEditPvp));
        OnPropertyChanged(nameof(CanEditCommandBlocks));
        OnPropertyChanged(nameof(CanEditSpawnMonsters));
        OnPropertyChanged(nameof(CanEditSeed));
        if (_server.Status == ServerStatus.Stopped)
        {
            Reload();
        }
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!_loading && e.PropertyName is { } name && SettingNames.Contains(name))
        {
            _ = SaveAsync();
        }
    }

    private async Task SaveAsync()
    {
        var edited = new GameSettings
        {
            GameMode = GameMode,
            Difficulty = Difficulty,
            Pvp = Pvp,
            MaxPlayers = Math.Clamp(MaxPlayers, 1, 1000),
            Motd = Motd,
            KeepInventory = KeepInventory,
            ViewDistance = Math.Clamp(ViewDistance, MinDistance, MaxDistance),
            SimulationDistance = Math.Clamp(SimulationDistance, MinDistance, MaxDistance),
            SpawnProtection = Math.Clamp(SpawnProtection, 0, 1000),
            CommandBlocks = CommandBlocks,
            AllowFlight = AllowFlight,
            SpawnMonsters = SpawnMonsters,
            Hardcore = Hardcore,
            Seed = Seed.Trim(),
        };

        var before = _loaded;
        var metadata = _server.Metadata;
        try
        {
            _server.UpdateMetadata(GameSettingsStore.Save(_server.InstanceDir, metadata, before, edited));
        }
        catch (IOException e)
        {
            _server.ShowError(string.Format(Text.Get("Settings_SaveFailed"), e.Message));
            return;
        }

        _loaded = edited;

        // Show the value that was saved, e.g. 50 view distance becomes 32.
        _loading = true;
        Show(edited);
        _loading = false;

        if (_server.IsOnline)
        {
            foreach (var command in GameSettingsStore.ChangedRuleCommands(metadata, before, edited))
            {
                await _server.SendCommandAsync(command);
            }
        }
    }

    private void Show(GameSettings settings)
    {
        GameMode = settings.GameMode;
        Difficulty = settings.Difficulty;
        Pvp = settings.Pvp;
        MaxPlayers = settings.MaxPlayers;
        Motd = settings.Motd;
        KeepInventory = settings.KeepInventory;
        ViewDistance = settings.ViewDistance;
        SimulationDistance = settings.SimulationDistance;
        SpawnProtection = settings.SpawnProtection;
        CommandBlocks = settings.CommandBlocks;
        AllowFlight = settings.AllowFlight;
        SpawnMonsters = settings.SpawnMonsters;
        Hardcore = settings.Hardcore;
        Seed = settings.Seed;
    }
}
