using WaNut.Core.Hosting;

namespace WaNut.Core.Tests.Hosting;

public class CpuMeterTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Sample_FirstSample_ReturnsNull()
    {
        var meter = new CpuMeter(4);

        Assert.Null(meter.Sample(TimeSpan.FromSeconds(10), Start));
    }

    [Fact]
    public void Sample_OneFullCoreOfEight_Returns12Point5()
    {
        var meter = new CpuMeter(8);
        meter.Sample(TimeSpan.FromSeconds(10), Start);

        var percent = meter.Sample(TimeSpan.FromSeconds(12), Start.AddSeconds(2));

        Assert.Equal(12.5, percent!.Value, precision: 6);
    }

    [Fact]
    public void Sample_MoreTimeThanCores_IsCappedAt100()
    {
        var meter = new CpuMeter(1);
        meter.Sample(TimeSpan.Zero, Start);

        Assert.Equal(100, meter.Sample(TimeSpan.FromSeconds(5), Start.AddSeconds(1)));
    }

    [Fact]
    public void Sample_SameMoment_ReturnsNull()
    {
        var meter = new CpuMeter(2);
        meter.Sample(TimeSpan.Zero, Start);

        Assert.Null(meter.Sample(TimeSpan.FromSeconds(1), Start));
    }
}
