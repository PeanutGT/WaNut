using WaNut.Core.Bridge;
using WaNut.Core.Instances;

namespace WaNut.Core.Tests.Bridge;

public sealed class BridgeManagerTests
{
    private static InstanceMetadata Server(string? vpsId = null) => new()
    {
        Id = "srv",
        Name = "Srv",
        Type = ServerType.Vanilla,
        MinecraftVersion = "1.21",
        JavaMajor = 21,
        MemoryMb = 2048,
        CreatedAt = DateTimeOffset.UnixEpoch,
        VpsId = vpsId,
    };

    [Fact]
    public void TargetFor_ServerOnThisPc_IsLocalAndStartOnBootPicksTheTask()
    {
        Assert.Equal(new DeploymentTarget(DeploymentKind.Local, null, BridgeRunMode.DetachedProcess), BridgeManager.TargetFor(Server(), startOnBoot: false));
        Assert.Equal(new DeploymentTarget(DeploymentKind.Local, null, BridgeRunMode.ScheduledTask), BridgeManager.TargetFor(Server(), startOnBoot: true));
    }

    [Fact]
    public void TargetFor_ServerOnAVps_IsThatVpsWhateverStartOnBootSays()
    {
        var target = BridgeManager.TargetFor(Server("abc123"), startOnBoot: true);

        Assert.Equal(DeploymentTarget.OnVps("abc123"), target);
        Assert.True(target.IsValid);
    }
}
