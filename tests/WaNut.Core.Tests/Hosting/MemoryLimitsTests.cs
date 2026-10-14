using WaNut.Core.Hosting;

namespace WaNut.Core.Tests.Hosting;

public sealed class MemoryLimitsTests
{
    [Theory]
    [InlineData(16384, 12288)]
    [InlineData(8192, 6144)]
    [InlineData(6000, 4096)]
    [InlineData(1024, 1024)]
    public void MaxServerMb_TotalRam_IsSeventyFivePercentRoundedDown(long totalMb, int expected)
    {
        Assert.Equal(expected, MemoryLimits.MaxServerMb(totalMb));
    }

    [Theory]
    [InlineData(16384, 4096)]
    [InlineData(4096, 3072)]
    [InlineData(1024, 1024)]
    public void DefaultServerMb_TotalRam_PrefersFourGigabytesWithinLimit(long totalMb, int expected)
    {
        Assert.Equal(expected, MemoryLimits.DefaultServerMb(totalMb));
    }

    [Fact]
    public void TotalPhysicalMb_ThisMachine_IsPositive()
    {
        Assert.True(MemoryLimits.TotalPhysicalMb() > 0);
    }
}
