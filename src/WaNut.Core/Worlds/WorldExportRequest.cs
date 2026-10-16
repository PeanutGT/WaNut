using WaNut.Core.Backups;

namespace WaNut.Core.Worlds;

/// <param name="Backup">The backup to export; null exports the server's current world.</param>
/// <param name="Destination">The .zip file to write, or the saves folder to add the world to.</param>
/// <param name="WorldName">The world's folder name in the zip or saves folder, e.g. the server's name.</param>
/// <param name="Player">Whose inventory and position become the single-player host's; null leaves level.dat as it is.</param>
public sealed record WorldExportRequest(BackupInfo? Backup, ExportTarget Target, string Destination, string WorldName, Guid? Player);
