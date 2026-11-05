using Renci.SshNet;
using WaNut.Core.Hosting;

namespace WaNut.Core.Remote.Run;

/// <summary>Runs servers on one of the user's own Linux hosts over SSH.</summary>
public sealed class RemoteServerRuntime(Func<CancellationToken, Task<SshClient>> connect) : IServerRuntime
{
    public RemoteServerRuntime(VpsConnector connector, VpsProfile profile)
        : this(cancellationToken => connector.ConnectAsync(profile, null, cancellationToken))
    {
    }

    public IRunningServer Create(ServerLaunch launch) => new RemoteServer(launch, connect);
}
