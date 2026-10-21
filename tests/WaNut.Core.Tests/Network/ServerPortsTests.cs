using WaNut.Core.Network;

namespace WaNut.Core.Tests.Network;

public sealed class ServerPortsTests
{
    [Fact]
    public void Pick_WantedPortUnused_KeepsIt()
    {
        Assert.Equal(25565, ServerPorts.Pick(25565, [25570], _ => true));
    }

    [Fact]
    public void Pick_AnotherServerHasIt_TakesNextUnused()
    {
        Assert.Equal(25567, ServerPorts.Pick(25565, [25565, 25566], _ => true));
    }

    [Fact]
    public void Pick_AnotherProgramListens_SkipsThatPort()
    {
        Assert.Equal(25566, ServerPorts.Pick(25565, [], port => port != 25565));
    }

    [Fact]
    public void Pick_NothingFreeInRange_ReturnsNull()
    {
        Assert.Null(ServerPorts.Pick(25565, [], _ => false));
    }

    [Fact]
    public void Pick_NearTopOfRange_NeverPassesLastPort()
    {
        Assert.Null(ServerPorts.Pick(65535, [65535], _ => true));
    }
}
