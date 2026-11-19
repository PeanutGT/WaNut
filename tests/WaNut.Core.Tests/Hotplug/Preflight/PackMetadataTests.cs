using WaNut.Core.Hotplug.Preflight;

namespace WaNut.Core.Tests.Hotplug.Preflight;

public sealed class PackMetadataTests
{
    [Fact]
    public void TryParse_PackFormatOnly_IsBothEnds()
    {
        var meta = PackMetadata.TryParse("""{"pack":{"pack_format":15,"description":"Loot"}}""", out _)!;
        Assert.Equal((15, 15, "Loot"), (meta.MinFormat, meta.MaxFormat, meta.Description));
    }

    [Theory]
    [InlineData("""{"pack":{"pack_format":18,"supported_formats":[15,26]}}""", 15, 26)]
    [InlineData("""{"pack":{"pack_format":18,"supported_formats":{"min_inclusive":16,"max_inclusive":41}}}""", 16, 41)]
    [InlineData("""{"pack":{"pack_format":18,"supported_formats":20}}""", 18, 20)]
    [InlineData("""{"pack":{"min_format":[88,0],"max_format":94}}""", 88, 94)]
    [InlineData("""{"pack":{"pack_format":81,"min_format":71,"max_format":[88,0]}}""", 71, 88)]
    public void TryParse_Ranges(string json, int min, int max)
    {
        var meta = PackMetadata.TryParse(json, out var problem)!;
        Assert.Null(problem);
        Assert.Equal((min, max), (meta.MinFormat!.Value, meta.MaxFormat!.Value));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"other":{}}""")]
    [InlineData("""{"pack":{"description":"x"}}""")]
    public void TryParse_Unusable_ReturnsNullWithReason(string json)
    {
        Assert.Null(PackMetadata.TryParse(json, out var problem));
        Assert.NotNull(problem);
    }

    [Fact]
    public void TryParse_TextComponentDescription_KeepsFormat()
    {
        var meta = PackMetadata.TryParse("""{"pack":{"pack_format":48,"description":{"text":"x"}}}""", out _)!;
        Assert.Null(meta.Description);
        Assert.Equal(48, meta.MinFormat);
    }
}
