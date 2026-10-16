namespace WaNut.Core.Worlds;

/// <param name="Path">The written zip file or world folder.</param>
/// <param name="InventoryProblem">Why the chosen player's inventory was not put into level.dat, when it wasn't.</param>
public sealed record WorldExportResult(string Path, InventoryProblem? InventoryProblem);
