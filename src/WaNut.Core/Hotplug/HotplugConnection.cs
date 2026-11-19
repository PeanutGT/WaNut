using WaNut.Core.Hosting;
using WaNut.Core.Remote.Files;
using WaNut.Core.Storage;

namespace WaNut.Core.Hotplug;

/// <summary>What a hot-plug works through: commands and files on the VPS, and the running server's console.</summary>
/// <param name="Files">The server folder on the VPS; its <see cref="IServerFileSystem.WorkspaceRoot"/> is the real path the commands use.</param>
public sealed record HotplugConnection(IRemoteCommandRunner Runner, IServerFileSystem Files, IServerConsole Console)
{
    public string Root => Files.WorkspaceRoot;
}
