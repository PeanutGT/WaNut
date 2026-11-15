using WaNut.Core.Hosting;

namespace WaNut.Core.Tests.Hosting;

public class IdleCpuWatchTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EmptyAndBusyForTheWholeDuration_WarnsOnce()
    {
        var watch = new IdleCpuWatch();

        Assert.False(watch.Update(true, 0, 50, Start));
        Assert.False(watch.Update(true, 0, 50, Start + IdleCpuWatch.Duration - TimeSpan.FromSeconds(1)));
        Assert.True(watch.Update(true, 0, 50, Start + IdleCpuWatch.Duration));
        Assert.False(watch.Update(true, 0, 50, Start + (IdleCpuWatch.Duration * 3)));
    }

    [Fact]
    public void CpuDropsInBetween_StartsCountingAgain()
    {
        var watch = new IdleCpuWatch();

        watch.Update(true, 0, 50, Start);
        watch.Update(true, 0, 5, Start + TimeSpan.FromMinutes(5));
        Assert.False(watch.Update(true, 0, 50, Start + IdleCpuWatch.Duration));
        Assert.True(watch.Update(true, 0, 50, Start + IdleCpuWatch.Duration + IdleCpuWatch.Duration));
    }

    [Fact]
    public void SomeoneOnline_NeverWarns()
    {
        var watch = new IdleCpuWatch();

        watch.Update(true, 2, 90, Start);
        Assert.False(watch.Update(true, 2, 90, Start + (IdleCpuWatch.Duration * 2)));
    }

    [Fact]
    public void AfterSomeoneJoined_CanWarnAgain()
    {
        var watch = new IdleCpuWatch();
        watch.Update(true, 0, 50, Start);
        Assert.True(watch.Update(true, 0, 50, Start + IdleCpuWatch.Duration));

        var later = Start + TimeSpan.FromHours(1);
        watch.Update(true, 1, 50, later);
        watch.Update(true, 0, 50, later + TimeSpan.FromMinutes(1));
        Assert.True(watch.Update(true, 0, 50, later + TimeSpan.FromMinutes(1) + IdleCpuWatch.Duration));
    }

    [Fact]
    public void Reset_ForWorldGeneration_StartsCountingAgain()
    {
        var watch = new IdleCpuWatch();
        watch.Update(true, 0, 50, Start);

        watch.Reset();
        Assert.False(watch.Update(true, 0, 50, Start + IdleCpuWatch.Duration));
    }

    [Theory]
    [InlineData("[12:00:00 INFO]: [Chunky] Task running for minecraft:overworld. Processed: 1200 chunks (3.2%)", true)]
    [InlineData("[12:00:00] [Worker-Main-2/INFO]: Preparing spawn area: 43%", true)]
    [InlineData("[12:00:00] [Server thread/INFO]: Steve joined the game", false)]
    public void IsWorldGeneration_KnowsPregenerationLines(string line, bool expected) =>
        Assert.Equal(expected, IdleCpuWatch.IsWorldGeneration(line));
}
