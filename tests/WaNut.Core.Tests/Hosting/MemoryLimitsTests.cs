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

    [Theory]
    [InlineData(4096, new[] { 4096 }, 16384, false)]
    [InlineData(7168, new[] { 4096, 3072 }, 16384, true)]
    [InlineData(5120, new[] { 4096 }, 12288, false)]
    [InlineData(6144, new int[0], 8192, false)]
    public void IsTooMuchTogether_RunningServers_LeavesWindowsThreeGigabytes(int startingMb, int[] runningMb, long totalMb, bool expected)
    {
        Assert.Equal(expected, MemoryLimits.IsTooMuchTogether(startingMb, runningMb, totalMb));
    }
}
