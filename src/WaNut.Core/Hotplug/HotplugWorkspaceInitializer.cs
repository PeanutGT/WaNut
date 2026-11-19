using WaNut.Core.Remote.Files;
using WaNut.Core.Storage;

namespace WaNut.Core.Hotplug;

/// <summary>
/// Makes the staging and backup folders a datapack hot-plug needs on the VPS. A rename is only one atomic step when
/// both ends are on the same mount, so when the server folder and the world's datapacks folder are on different
/// mounts (or <c>df</c> can't say), the folders go under the world folder instead.
/// </summary>
public static class HotplugWorkspaceInitializer
{
    /// <param name="root">The server folder's real path on the VPS (<see cref="RemoteVpsAdapter.Root"/>).</param>
    /// <param name="worldFolder">The world folder, relative to <paramref name="root"/>: the server's <c>level-name</c>.</param>
    /// <exception cref="DirectoryNotFoundException">The server folder doesn't exist.</exception>
    /// <exception cref="WorkspaceRootException">The server folder became another folder.</exception>
    /// <exception cref="HotplugWorldMissingException">The world folder doesn't exist yet.</exception>
    public static async Task<HotplugWorkspace> InitializeAsync(IRemoteCommandRunner runner, string root, string worldFolder = "world", CancellationToken cancellationToken = default)
    {
        var world = ServerPath.Normalize(worldFolder);
        if (world.Length == 0)
        {
            throw new ArgumentException("A world folder is needed.", nameof(worldFolder));
        }

        var probe = await runner.RunAsync(HotplugCommands.Probe(root, world), cancellationToken: cancellationToken).ConfigureAwait(false);
        Check(probe, root, world, "Checking the mounts");
        var parts = probe.Output.Split('\0');
        var sameMount = parts.Length == 2 && parts[0].Length > 0 && parts[0] == parts[1];

        var workspace = Plan(world, sameMount);
        Check(await runner.RunAsync(HotplugCommands.Create(root, workspace), cancellationToken: cancellationToken).ConfigureAwait(false), root, world, "Making the staging folders");
        return workspace;
    }

    /// <summary>The folders to use: next to the server folder when the mounts match, else under the world folder.</summary>
    public static HotplugWorkspace Plan(string world, bool sameMount)
    {
        var parent = sameMount ? string.Empty : world + "/";
        return new HotplugWorkspace(
            $"{parent}{HotplugWorkspace.StagingName}/{HotplugWorkspace.DatapacksName}",
            $"{parent}{HotplugWorkspace.BackupName}/{HotplugWorkspace.DatapacksName}",
            $"{world}/{HotplugWorkspace.DatapacksName}",
            UsedWorldFallback: !sameMount);
    }

    private static void Check(RemoteCommandResult result, string root, string world, string what)
    {
        switch (result.ExitStatus)
        {
            case 0:
                return;
            case RemoteFileCommands.NoFolderExit:
                throw new DirectoryNotFoundException($"'{root}' no longer exists on the VPS.");
            case RemoteFileCommands.MovedExit:
                throw new WorkspaceRootException(root, WorkspaceRootProblem.Moved);
            case HotplugCommands.NoWorldExit:
                throw new HotplugWorldMissingException(world);
            default:
                throw new InvalidOperationException($"{what} on the VPS failed ({result.ExitStatus}): {result.Error.Trim()}");
        }
    }
}
