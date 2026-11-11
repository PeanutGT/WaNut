using WaNut.Core;
using WaNut.Core.Remote;
using WaNut.Core.Remote.Run;

namespace WaNut.Core.Tests.Remote.Run;

public sealed class VpsMemoryTests
{
    [Fact]
    public void CheckMemory_WithinThreeQuartersOfVps_Passes()
    {
        VpsLaunchPlanner.CheckMemory(9216, 12288);
        VpsLaunchPlanner.CheckMemory(1024, 2048);
    }

    [Fact]
    public void CheckMemory_MoreThanVpsCanGive_RefusesWithItsLimit()
    {
        var e = Assert.Throws<VpsMemoryException>(() => VpsLaunchPlanner.CheckMemory(9728, 12288));

        Assert.Equal(12288, e.VpsMemoryMb);
        Assert.Equal(9216, e.MaxServerMb);
    }

    [Fact]
    public void CheckMemory_RamNotRead_LetsItThrough() =>
        VpsLaunchPlanner.CheckMemory(16384, 0);

    [Fact]
    public void HostInfo_MemoryMb_FromProbedBytes() =>
        Assert.Equal(11_979, HostProbe.Parse("memkb=12266848\n").MemoryMb);

    [Fact]
    public void VpsProfile_MemoryMb_SurvivesConfigJson()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var store = new Core.Settings.AppSettingsStore(new LauncherPaths(dir));
            store.Save(new Core.Settings.AppSettings { Vps = [new VpsProfile { Name = "Oracle", MemoryMb = 12288 }] });

            Assert.Equal(12288, store.Load().Vps[0].MemoryMb);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
