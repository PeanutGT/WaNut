using WaNut.App.ViewModels;
using WaNut.Core.Backups;
using WaNut.Core.Instances;
using WaNut.Core.Worlds;

namespace WaNut.App.Services;

public interface IDialogService
{
    /// <summary>Shows the Create Server wizard. Returns the new server, or null if the user cancelled.</summary>
    InstanceMetadata? ShowCreateServer();

    /// <summary>Shows the "Change type or version" window. Returns the changed server, or null if nothing changed.</summary>
    InstanceMetadata? ShowChangeServer(InstanceMetadata server, string instanceDir);

    /// <summary>Shows the App settings window.</summary>
    void ShowAppSettings();

    /// <summary>Shows the "Replace with another world" window. Returns what was imported, or null if nothing changed.</summary>
    WorldImportResult? ShowImportWorld(ServerViewModel server);

    /// <summary>Shows the "Export world" window for the current world or a backup. Returns null if nothing was exported.</summary>
    WorldExportResult? ShowExportWorld(ServerViewModel server, BackupInfo? backup);

    /// <summary>Shows the "Delete server" window. Returns true when the server was deleted; <paramref name="keptZip"/> is the world zip kept, if any.</summary>
    bool ShowDeleteServer(ServerViewModel server, out string? keptZip);

    /// <summary>Shows the "Duplicate server" window. Returns the new server, or null if nothing was copied.</summary>
    InstanceMetadata? ShowDuplicateServer(ServerViewModel server);

    /// <summary>Asks before resetting the world. Returns the seed for the new world (empty for random), or null to cancel.</summary>
    string? AskResetWorld(string serverName);

    /// <summary>Asks a yes/no question. Returns true for yes.</summary>
    bool Confirm(string message);

    /// <summary>Shows a message with a Close button.</summary>
    void Inform(string message);

    /// <summary>Asks for a world folder. Returns null if the user cancelled.</summary>
    string? PickFolder(string? title = null);

    /// <summary>Asks for a picture (PNG, JPEG, BMP or GIF). Returns null if the user cancelled.</summary>
    string? PickImage();

    /// <summary>Asks for a .zip file to read. Returns null if the user cancelled.</summary>
    string? PickZipToOpen();

    /// <summary>Asks where to save a .zip file. Returns null if the user cancelled.</summary>
    string? PickZipToSave(string fileName);
}
