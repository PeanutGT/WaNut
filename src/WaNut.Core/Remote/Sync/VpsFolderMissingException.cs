namespace WaNut.Core.Remote.Sync;

/// <summary>
/// The VPS has no folder for this server, or one without server.properties, so there is nothing to bring back.
/// Thrown before anything on this PC is touched.
/// </summary>
public sealed class VpsFolderMissingException()
    : IOException("The VPS has no files for this server. Nothing on this PC was changed.");
