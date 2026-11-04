namespace WaNut.Core.Hosting;

/// <summary>
/// Placeholder for running a server on the user's own Linux host over SSH. Not wired into the app yet.
/// </summary>
public sealed class RemoteServerRuntime : IServerRuntime
{
    public IRunningServer Create(ServerLaunch launch) =>
        throw new NotSupportedException("Running a server on a remote host is not available yet.");
}
