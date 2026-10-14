using WaNut.Core.Instances;

namespace WaNut.App.Services;

public interface IDialogService
{
    /// <summary>Shows the Create Server wizard. Returns the new server, or null if the user cancelled.</summary>
    InstanceMetadata? ShowCreateServer();

    /// <summary>Asks a yes/no question. Returns true for yes.</summary>
    bool Confirm(string message);
}
