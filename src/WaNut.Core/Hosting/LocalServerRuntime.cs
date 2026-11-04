namespace WaNut.Core.Hosting;

/// <summary>Runs servers as a hidden java.exe on this PC.</summary>
public sealed class LocalServerRuntime : IServerRuntime
{
    public IRunningServer Create(ServerLaunch launch) => new ServerProcess(launch);
}
