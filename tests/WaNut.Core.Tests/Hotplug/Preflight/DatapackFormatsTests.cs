using WaNut.Core.Hotplug.Preflight;

namespace WaNut.Core.Tests.Hotplug.Preflight;

public sealed class DatapackFormatsTests
{
    [Theory]
    [InlineData("1.16.5", 6)]
    [InlineData("1.17.1", 7)]
    [InlineData("1.18.2", 9)]
    [InlineData("1.19.4", 12)]
    [InlineData("1.20.1", 15)]
    [InlineData("1.20.4", 26)]
    [InlineData("1.20.6", 41)]
    [InlineData("1.21", 48)]
    [InlineData("1.21.1", 48)]
    [InlineData("1.21.4", 61)]
    [InlineData("1.21.8", 81)]
    [InlineData("1.21.10", 88)]
    public void For_KnownVersion_ReturnsFormat(string version, int format) => Assert.Equal(format, DatapackFormats.For(version));

    [Theory]
    [InlineData("1.12.2")]
    [InlineData("1.21.11")]
    [InlineData("26.1")]
    [InlineData("snapshot")]
    public void For_OutsideTable_ReturnsNull(string version) => Assert.Null(DatapackFormats.For(version));

    [Theory]
    [InlineData(48, 48, 48, FormatCheck.Matches)]
    [InlineData(41, 57, 48, FormatCheck.Matches)]
    [InlineData(15, 15, 48, FormatCheck.Mismatch)]
    public void Check_ComparesRange(int min, int max, int expected, FormatCheck result) =>
        Assert.Equal(result, DatapackFormats.Check(new PackMetadata(min, max, null), expected));

    [Fact]
    public void Check_UnknownServer_Unknown() => Assert.Equal(FormatCheck.Unknown, DatapackFormats.Check(new PackMetadata(48, 48, null), null));
}
