using WaNut.Core.Hosting;

namespace WaNut.Core.Tests.Hosting;

public sealed class UsageHistoryTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Add_KeepsOnlyTheLastTenMinutes()
    {
        var history = new UsageHistory();

        history.Add(new UsageSample(Start, 90, 100));
        history.Add(new UsageSample(Start.AddMinutes(5), 20, 200));
        history.Add(new UsageSample(Start.AddMinutes(11), 30, 300));

        Assert.Equal([20, 30], history.Samples.Select(s => s.CpuPercent));
        Assert.Equal(30, history.PeakCpu);
        Assert.Equal(300, history.PeakMemory);
        Assert.Equal(300, history.Latest!.Value.MemoryBytes);
    }

    [Fact]
    public void Clear_Empties()
    {
        var history = new UsageHistory();
        history.Add(new UsageSample(Start, 50, 100));

        history.Clear();

        Assert.Empty(history.Samples);
        Assert.Null(history.Latest);
        Assert.Equal(0, history.PeakCpu);
    }

    [Theory]
    [InlineData(0, UsageLoad.Calm)]
    [InlineData(39.9, UsageLoad.Calm)]
    [InlineData(40, UsageLoad.Busy)]
    [InlineData(74.9, UsageLoad.Busy)]
    [InlineData(75, UsageLoad.Hot)]
    [InlineData(100, UsageLoad.Hot)]
    public void LoadOf_SortsCpuShare(double cpu, UsageLoad expected) => Assert.Equal(expected, UsageHistory.LoadOf(cpu));
}
