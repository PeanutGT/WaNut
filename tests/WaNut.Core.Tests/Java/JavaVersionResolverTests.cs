using WaNut.Core.Java;

namespace WaNut.Core.Tests.Java;

public sealed class JavaVersionResolverTests
{
    [Theory]
    [InlineData("1.16.5", 8)]
    [InlineData("1.17", 17)]
    [InlineData("1.17.1", 17)]
    [InlineData("1.18.2", 17)]
    [InlineData("1.20.4", 17)]
    [InlineData("1.20.5", 21)]
    [InlineData("1.21.1", 21)]
    [InlineData("26.1", 25)]
    [InlineData("26.3", 25)]
    [InlineData("nonsense", 21)]
    public void FallbackFor_Version_ReturnsExpectedJava(string minecraftVersion, int expected)
    {
        Assert.Equal(expected, JavaVersionResolver.FallbackFor(minecraftVersion));
    }
}
