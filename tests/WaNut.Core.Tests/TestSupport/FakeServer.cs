using WaNut.Core.Hosting;

namespace WaNut.Core.Tests.TestSupport;

/// <summary>Launches tests/FakeMinecraftServer, which prints Minecraft-style console lines.</summary>
internal static class FakeServer
{
    public static ServerLaunch Launch(string workingDirectory, params string[] extraArgs)
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "FakeMinecraftServer.dll");
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        return new ServerLaunch(dotnet, [dll, .. extraArgs], workingDirectory);
    }

    public static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 15_000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException("The fake server did not reach the expected state in time.");
            }

            await Task.Delay(20);
        }
    }
}
