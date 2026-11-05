using Renci.SshNet;
using WaNut.Core.Hosting;

namespace WaNut.Core.Remote.Run;

/// <summary>
/// Runs servers on one of the user's own Linux hosts over SSH. With <paramref name="runnerPid"/> the server it creates
/// follows a run that is already going there instead of starting one.
/// </summary>
public sealed class RemoteServerRuntime(Func<CancellationToken, Task<SshClient>> connect, int? runnerPid = null) : IServerRuntime
{
    public RemoteServerRuntime(VpsConnector connector, VpsProfile profile, int? runnerPid = null)
        : this(cancellationToken => connector.ConnectAsync(profile, null, cancellationToken), runnerPid)
    {
    }

    public IRunningServer Create(ServerLaunch launch) => new RemoteServer(launch, connect, runnerPid);
}
