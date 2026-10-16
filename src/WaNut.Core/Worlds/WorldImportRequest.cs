using WaNut.Core.Instances;

namespace WaNut.Core.Worlds;

/// <param name="Type">The server's type, which decides where the Nether and End go.</param>
/// <param name="MinecraftVersion">The server's Minecraft version.</param>
/// <param name="OwnerName">The host's Minecraft name; their single-player inventory moves to this player.</param>
/// <param name="BringInventory">Carry the host's inventory and position over from level.dat.</param>
public sealed record WorldImportRequest(WorldSource Source, ServerType Type, string MinecraftVersion, string? OwnerName, bool BringInventory);
