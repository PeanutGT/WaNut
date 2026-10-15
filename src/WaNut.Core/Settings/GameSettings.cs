namespace WaNut.Core.Settings;

public enum GameMode
{
    Survival,
    Creative,
    Adventure,
    Spectator,
}

public enum Difficulty
{
    Peaceful,
    Easy,
    Normal,
    Hard,
}

/// <summary>
/// The settings on the Game settings tab. Most live in server.properties; keepInventory, and on newer versions
/// PvP, command blocks and monster spawning, are game rules that WaNut sends to the running server.
/// </summary>
public sealed record GameSettings
{
    public GameMode GameMode { get; init; } = GameMode.Survival;

    public Difficulty Difficulty { get; init; } = Difficulty.Easy;

    public bool Pvp { get; init; } = true;

    public int MaxPlayers { get; init; } = 20;

    public string Motd { get; init; } = "A Minecraft Server";

    public bool KeepInventory { get; init; }

    public int ViewDistance { get; init; } = 10;

    public int SimulationDistance { get; init; } = 10;

    public int SpawnProtection { get; init; } = 16;

    public bool CommandBlocks { get; init; }

    public bool AllowFlight { get; init; }

    public bool SpawnMonsters { get; init; } = true;

    public bool Hardcore { get; init; }

    /// <summary>Only used when the world is first generated.</summary>
    public string Seed { get; init; } = string.Empty;
}
