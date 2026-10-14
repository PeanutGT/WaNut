using WaNut.Core.Downloads;
using WaNut.Core.Instances;
using WaNut.Core.Java;
using WaNut.Core.Jvm;

namespace WaNut.Core.Hosting;

/// <summary>
/// Gets a server ready to start: checks its files, makes sure its Java is installed and builds the java.exe command.
/// </summary>
public sealed class ServerLaunchPlanner(InstanceStore store, JavaRuntimeManager java)
{
    public async Task<ServerLaunch> PrepareAsync(
        InstanceMetadata server,
        IProgress<DownloadProgress>? javaProgress = null,
        CancellationToken cancellationToken = default)
    {
        var dir = store.InstanceDir(server.Id);
        if (!File.Exists(Path.Combine(dir, server.ServerJar)))
        {
            throw new ServerStartException(ServerStartProblem.ServerJarMissing);
        }

        if (!EulaFile.IsAccepted(dir))
        {
            throw new ServerStartException(ServerStartProblem.EulaNotAccepted);
        }

        // Normally installed when the server was created; downloaded again if the runtime folder was removed.
        var javaExe = await java.EnsureInstalledAsync(server.JavaMajor, javaProgress, cancellationToken).ConfigureAwait(false);
        return new ServerLaunch(javaExe, JvmArguments.Build(server.JavaMajor, server.MemoryMb, server.ServerJar), dir);
    }
}
