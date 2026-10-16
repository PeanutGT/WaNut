using WaNut.Core.Players;
using WaNut.Core.Tests.TestSupport;

namespace WaNut.Core.Tests.Worlds;

public sealed class UserCacheTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Read_ServerCache_ReturnsPlayersByName()
    {
        File.WriteAllText(Path.Combine(_temp.Path, "usercache.json"), """
            [{"name":"zed","uuid":"069a79f4-44e9-4726-a5be-fca90e38aaf5","expiresOn":"2026-11-01 10:00:00 +0800"},
             {"name":"Alex","uuid":"b50ad385-829d-3141-a216-7e7d7539ba7f"},
             {"name":"broken","uuid":"nope"}]
            """);

        Assert.Equal(["Alex", "zed"], UserCache.Read(_temp.Path).Select(p => p.Name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{not json")]
    [InlineData("{}")]
    public void Read_MissingOrDamaged_ReturnsEmpty(string? text)
    {
        if (text is not null)
        {
            File.WriteAllText(Path.Combine(_temp.Path, "usercache.json"), text);
        }

        Assert.Empty(UserCache.Read(_temp.Path));
    }
}
