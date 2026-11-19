using WaNut.Core.Remote.Sync;

namespace WaNut.Core.Tests.Remote.Sync;

public sealed class SyncRulesHotplugTests
{
    [Theory]
    [InlineData(".nut-staging/datapacks/a.zip.part")]
    [InlineData(".nut-backup/datapacks/a.20261119T064152Z.zip")]
    [InlineData("world/.nut-staging/datapacks/a.zip")]
    [InlineData(".nut-staging/.lock")]
    public void IsIgnored_HotplugFolders_True(string path) => Assert.True(SyncRules.IsIgnored(path));

    [Fact]
    public void IsIgnored_WorldDatapack_False() => Assert.False(SyncRules.IsIgnored("world/datapacks/a.zip"));
}
