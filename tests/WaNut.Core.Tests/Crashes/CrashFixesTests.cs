using WaNut.Core.Crashes;

namespace WaNut.Core.Tests.Crashes;

public class CrashFixesTests
{
    [Theory]
    [InlineData("fabric-api", "fabric-api")]
    [InlineData("fabric", "fabric-api")]
    [InlineData("fabric-networking-api-v1", "fabric-api")]
    [InlineData("fabric-language-kotlin", "fabric-language-kotlin")]
    [InlineData("architectury", "architectury-api")]
    [InlineData("cloth_config", "cloth-config")]
    [InlineData("Some_Mod", "some-mod")]
    public void ModrinthProjectFor_ModId_ReturnsSlug(string modId, string expected)
    {
        Assert.Equal(expected, CrashFixes.ModrinthProjectFor(modId));
    }

    [Fact]
    public void NextFreePort_SomeTaken_ReturnsFirstFreeAfterCurrent()
    {
        Assert.Equal(25568, CrashFixes.NextFreePort(25565, port => port >= 25568));
    }

    [Fact]
    public void NextFreePort_NoneFree_ReturnsNull()
    {
        Assert.Null(CrashFixes.NextFreePort(25565, _ => false));
    }
}
