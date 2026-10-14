using WaNut.Core.Instances;

namespace WaNut.App.Services;

public interface IDialogService
{
    /// <summary>Shows the Create Server wizard. Returns the new server, or null if the user cancelled.</summary>
    InstanceMetadata? ShowCreateServer();
}
