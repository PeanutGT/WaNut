using WaNut.Bridge.Relay;

namespace WaNut.Bridge.Tests.Relay;

public sealed class PlayerListTests
{
    [Fact]
    public void TryParse_Vanilla_ReadsCountsAndNames()
    {
        var list = PlayerList.TryParse("There are 2 of a max of 20 players online: Steve, Alex");

        Assert.Equal((2, 20), (list!.Online, list.Max));
        Assert.Equal(["Steve", "Alex"], list.Names);
    }

    [Fact]
    public void TryParse_NobodyOnline_ReturnsEmptyNames()
    {
        var list = PlayerList.TryParse("There are 0 of a max of 10 players online: ");

        Assert.Equal((0, 10), (list!.Online, list.Max));
        Assert.Empty(list.Names);
    }

    [Fact]
    public void TryParse_BukkitWithGroupsAndColors_ReadsNames()
    {
        var list = PlayerList.TryParse("§6There are §c2§6 out of maximum §c20§6 players online.\n§6default§r: Steve, Alex");

        Assert.Equal((2, 20), (list!.Online, list.Max));
        Assert.Equal(["Steve", "Alex"], list.Names);
    }

    [Fact]
    public void TryParse_UnknownText_ReturnsNull()
    {
        Assert.Null(PlayerList.TryParse("Unknown command"));
    }
}
