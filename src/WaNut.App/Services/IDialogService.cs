using WaNut.Core.Instances;

namespace WaNut.App.Services;

public interface IDialogService
{
    /// <summary>Shows the Create Server wizard. Returns the new server, or null if the user cancelled.</summary>
    InstanceMetadata? ShowCreateServer();

    /// <summary>Shows the "Change type or version" window. Returns the changed server, or null if nothing changed.</summary>
    InstanceMetadata? ShowChangeServer(InstanceMetadata server, string instanceDir);

    /// <summary>Shows the App settings window.</summary>
    void ShowAppSettings();

    /// <summary>Asks a yes/no question. Returns true for yes.</summary>
    bool Confirm(string message);
}
