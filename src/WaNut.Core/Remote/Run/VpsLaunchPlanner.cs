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
        CancellationToken cancellationToken = default)
    {
        var dir = store.InstanceDir(server.Id);
        ServerLaunchPlanner.CheckFiles(dir, server);

        steps?.Report(VpsStep.Connecting);
        using (var ssh = await connector.ConnectAsync(vps, null, cancellationToken).ConfigureAwait(false))
        {
            await VpsConnector.RunAsync(ssh, RunnerScript.InstallCommand(), cancellationToken).ConfigureAwait(false);
            if ((await VpsConnector.RunAsync(ssh, RemoteJava.IsInstalledCommand(server.JavaMajor), cancellationToken).ConfigureAwait(false)).Trim() != "yes")
            {
                steps?.Report(VpsStep.PreparingJava);
                await InstallJavaAsync(ssh, server.JavaMajor, cancellationToken).ConfigureAwait(false);
            }
        }

        steps?.Report(VpsStep.Uploading);
        await SyncAsync(server.Id, vps, SyncDirection.Upload, upload, cancellationToken).ConfigureAwait(false);

        var custom = JvmArguments.ParseCustom(server.CustomJvmArguments, out _) ?? [];
        var arguments = JvmArguments.Build(server.JavaMajor, server.MemoryMb, ForLinux(server.ServerJar), custom);
        return new ServerLaunch(RemoteJava.JavaPath(server.JavaMajor), arguments, SftpSyncTree.ServerRoot(server.Id));
    }

    /// <summary>Makes this PC's folder match the VPS copy, after the server stopped there.</summary>
    public Task DownloadAsync(InstanceMetadata server, VpsProfile vps, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default) =>
        SyncAsync(server.Id, vps, SyncDirection.Download, progress, cancellationToken);

    /// <summary>Forge and NeoForge write both argument files; Linux needs the one with ':' between classpath entries.</summary>
    public static string ForLinux(string serverJar) =>
        serverJar.StartsWith('@') && serverJar.EndsWith("/win_args.txt", StringComparison.Ordinal)
            ? serverJar[..^"win_args.txt".Length] + "unix_args.txt"
            : serverJar;

    private async Task InstallJavaAsync(SshClient ssh, int major, CancellationToken cancellationToken)
    {
        var info = HostProbe.Parse(await VpsConnector.RunAsync(ssh, HostProbe.Command, cancellationToken).ConfigureAwait(false));
        var package = await adoptium.GetLatestJreAsync(major, "linux", RemoteJava.AdoptiumArchitecture(info.Architecture), cancellationToken).ConfigureAwait(false);
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
