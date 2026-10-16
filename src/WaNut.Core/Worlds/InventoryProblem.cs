namespace WaNut.Core.Worlds;

/// <summary>Why the host's inventory and position were not carried over. The world itself still was.</summary>
public enum InventoryProblem
{
    /// <summary>Mojang has no account with the host's name.</summary>
    NameNotFound,

    /// <summary>Mojang could not be reached to look the name up.</summary>
    LookupFailed,

    /// <summary>The world holds no single-player inventory, e.g. it came from a server.</summary>
    NoPlayerInWorld,

    /// <summary>The chosen player has never played in this world.</summary>
    NoPlayerData,

    /// <summary>The player data could not be read.</summary>
    DamagedPlayerData,
}
