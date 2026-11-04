namespace WaNut.Core.Hosting;

/// <summary>Where a server runs: decides what a launch turns into. <see cref="ServerHost"/> starts what it returns.</summary>
public interface IServerRuntime
{
    /// <summary>A server for <paramref name="launch"/> that has not started yet, so callers can subscribe first.</summary>
    IRunningServer Create(ServerLaunch launch);
}
