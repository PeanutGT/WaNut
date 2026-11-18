namespace WaNut.Core.Remote.Files;

/// <summary>
/// Keeps a mistyped or misconfigured server folder from turning into a delete of system or home folders on the VPS.
/// <see cref="Check"/> runs on the folder's real path (links resolved) when it is opened; the marker files are
/// checked on the VPS right before each delete (see <see cref="RemoteFileCommands.Delete"/>).
/// </summary>
public static class WorkspaceRootGuard
{
    /// <summary>A server folder has at least one of these. Folders end with '/'.</summary>
    public static readonly IReadOnlyList<string> Markers = ["server.properties", "eula.txt", "libraries/"];

    // Their whole tree belongs to the system; a Minecraft server never lives inside them.
    private static readonly string[] SystemTrees = ["bin", "boot", "dev", "etc", "lib", "lib32", "lib64", "libx32", "proc", "run", "sbin", "sys", "usr"];

    // Folders that hold many unrelated folders one level down (/var/lib, /home/ubuntu): need one more level.
    private static readonly string[] SharedParents = ["home", "var"];

    /// <param name="root">The folder's real absolute path on the VPS.</param>
    /// <param name="home">The SSH user's real home folder.</param>
    /// <exception cref="WorkspaceRootException">The folder must not be managed.</exception>
    public static void Check(string root, string home)
    {
        var problem = Find(root, home);
        if (problem is not null)
        {
            throw new WorkspaceRootException(root, problem.Value);
        }
    }

    /// <summary>What is wrong with <paramref name="root"/>, or null when WaNut may manage it.</summary>
    public static WorkspaceRootProblem? Find(string root, string home)
    {
        var path = Normalize(root);
        if (path is null)
        {
            return WorkspaceRootProblem.SystemFolder;
        }

        var homePath = Normalize(home);
        if (homePath is not null && (path == homePath || homePath.StartsWith(path + "/", StringComparison.Ordinal)))
        {
            return WorkspaceRootProblem.HomeFolder;
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2 || SystemTrees.Contains(segments[0], StringComparer.Ordinal))
        {
            return WorkspaceRootProblem.SystemFolder;
        }

        if (segments.Length == 2 && SharedParents.Contains(segments[0], StringComparer.Ordinal))
        {
            return segments[0] == "home" ? WorkspaceRootProblem.HomeFolder : WorkspaceRootProblem.SystemFolder;
        }

        return null;
    }

    /// <summary>"/a/b" without empty, "." or trailing segments; null when the path isn't absolute or has "..".</summary>
    private static string? Normalize(string path)
    {
        if (string.IsNullOrEmpty(path) || !path.StartsWith('/') || path.Contains('\0', StringComparison.Ordinal))
        {
            return null;
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries).Where(s => s != ".").ToList();
        return segments.Contains("..") ? null : "/" + string.Join('/', segments);
    }
}
