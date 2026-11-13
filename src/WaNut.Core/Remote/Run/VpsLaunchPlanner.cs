using Renci.SshNet;
using WaNut.Core.Downloads;
using WaNut.Core.Hosting;
using WaNut.Core.Instances;
using WaNut.Core.Java;
using WaNut.Core.Jvm;
using WaNut.Core.Remote.Sync;

namespace WaNut.Core.Remote.Run;

/// <summary>
/// Gets a server ready to start on the user's VPS: checks its files here, makes sure the VPS has the runner script and
/// the right Java, uploads what changed and builds the command. Bringing the folder back is <see cref="DownloadAsync"/>.
/// </summary>
public sealed class VpsLaunchPlanner(InstanceStore store, VpsConnector connector, AdoptiumClient adoptium)
{
    public async Task<ServerLaunch> PrepareAsync(
        InstanceMetadata server,
        VpsProfile vps,
        IProgress<VpsStep>? steps = null,
        IProgress<DownloadProgress>? upload = null,
        IProgress<long>? memoryRead = null,
        CancellationToken cancellationToken = default)
    {
        var dir = store.InstanceDir(server.Id);
        ServerLaunchPlanner.CheckFiles(dir, server);

        steps?.Report(VpsStep.Connecting);
        using (var ssh = await connector.ConnectAsync(vps, null, cancellationToken).ConfigureAwait(false))
        {
            var info = HostProbe.Parse(await VpsConnector.RunAsync(ssh, HostProbe.Command, cancellationToken).ConfigureAwait(false));
            memoryRead?.Report(info.MemoryMb);
            CheckMemory(server.MemoryMb, info.MemoryMb);

            await VpsConnector.RunAsync(ssh, RunnerScript.InstallCommand(), cancellationToken).ConfigureAwait(false);
            if ((await VpsConnector.RunAsync(ssh, RemoteJava.IsInstalledCommand(server.JavaMajor), cancellationToken).ConfigureAwait(false)).Trim() != "yes")
            {
                steps?.Report(VpsStep.PreparingJava);
                await InstallJavaAsync(ssh, server.JavaMajor, info.Architecture, cancellationToken).ConfigureAwait(false);
            }
        }

        steps?.Report(VpsStep.Uploading);
        await SyncAsync(server.Id, vps, SyncDirection.Upload, upload, cancellationToken).ConfigureAwait(false);

        var custom = JvmArguments.ParseCustom(server.CustomJvmArguments, out _) ?? [];
        var arguments = JvmArguments.Build(server.JavaMajor, server.MemoryMb, ForLinux(server.ServerJar), custom);
        return new ServerLaunch(RemoteJava.JavaPath(server.JavaMajor), arguments, SftpSyncTree.ServerRoot(server.Id));
    }

    /// <summary>What is on the VPS for this server right now: still running, ended while WaNut was away, or nothing.</summary>
    public async Task<VpsRun> CheckRunAsync(InstanceMetadata server, VpsProfile vps, CancellationToken cancellationToken = default)
    {
        using var ssh = await connector.ConnectAsync(vps, null, cancellationToken).ConfigureAwait(false);
        return VpsRun.Parse(await VpsConnector.RunAsync(ssh, RunnerScript.ProbeCommand(SftpSyncTree.ServerRoot(server.Id)), cancellationToken).ConfigureAwait(false));
    }

    /// <summary>The launch to follow a server already running on the VPS: only its folder matters, nothing is started.</summary>
    public static ServerLaunch AttachLaunch(InstanceMetadata server) =>
        new(RemoteJava.JavaPath(server.JavaMajor), [], SftpSyncTree.ServerRoot(server.Id));

    /// <summary>Makes this PC's folder match the VPS copy, after the server stopped there.</summary>
    public Task DownloadAsync(InstanceMetadata server, VpsProfile vps, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default) =>
        SyncAsync(server.Id, vps, SyncDirection.Download, progress, cancellationToken);

    /// <summary>Forge and NeoForge write both argument files; Linux needs the one with ':' between classpath entries.</summary>
    public static string ForLinux(string serverJar) =>
        serverJar.StartsWith('@') && serverJar.EndsWith("/win_args.txt", StringComparison.Ordinal)
            ? serverJar[..^"win_args.txt".Length] + "unix_args.txt"
            : serverJar;

    /// <summary>
    /// Stops the start when the server asks for more than <see cref="MemoryLimits.MaxVpsServerMb"/> of the VPS's RAM, so
    /// Java doesn't fail to get its heap or get killed for memory later. The number is never lowered behind the user's back. 0 (RAM not read)
    /// lets it through.
    /// </summary>
    /// <exception cref="VpsMemoryException">Too much for this VPS.</exception>
    public static void CheckMemory(int serverMb, long vpsMemoryMb)
    {
        if (vpsMemoryMb > 0 && serverMb > MemoryLimits.MaxVpsServerMb(vpsMemoryMb))
        {
            throw new VpsMemoryException(vpsMemoryMb);
        }
    }

    private async Task InstallJavaAsync(SshClient ssh, int major, HostArchitecture architecture, CancellationToken cancellationToken)
    {
        var package = await adoptium.GetLatestJreAsync(major, "linux", RemoteJava.AdoptiumArchitecture(architecture), cancellationToken).ConfigureAwait(false);
        await VpsConnector.RunAsync(ssh, RemoteJava.InstallCommand(package), cancellationToken).ConfigureAwait(false);
    }

    private async Task SyncAsync(string id, VpsProfile vps, SyncDirection direction, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        using var sftp = await connector.ConnectSftpAsync(vps, cancellationToken).ConfigureAwait(false);
        var local = new LocalSyncTree(store.InstanceDir(id));
        var remote = new SftpSyncTree(sftp, SftpSyncTree.ServerRoot(id));
        if (direction == SyncDirection.Upload)
        {
            await FolderSync.RunAsync(local, remote, direction, progress, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await FolderSync.RunAsync(remote, local, direction, progress, cancellationToken).ConfigureAwait(false);
        }
    }
}
