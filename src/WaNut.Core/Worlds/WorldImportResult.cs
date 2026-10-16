using WaNut.Core.Backups;

namespace WaNut.Core.Worlds;

/// <param name="InventoryMoved">True when the host's single-player inventory now belongs to their server player.</param>
/// <param name="InventoryProblem">Why it was not moved, when it was asked for and failed.</param>
/// <param name="HasResourcePack">The world brings a resources.zip, which friends need to install themselves.</param>
/// <param name="SafetyBackup">The backup of the world that was replaced; null when the server had none.</param>
public sealed record WorldImportResult(bool InventoryMoved, InventoryProblem? InventoryProblem, bool HasResourcePack, BackupInfo? SafetyBackup);
