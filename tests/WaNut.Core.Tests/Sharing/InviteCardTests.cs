using WaNut.Core.Sharing;

namespace WaNut.Core.Tests.Sharing;

public class InviteCardTests
{
    private static readonly InviteCardLabels Labels = new("Join {0}!", "Minecraft {0}", "Address:", "none yet", "Network: {0}", "Mods:");

    [Fact]
    public void Compose_FullCard_ListsAddressesNoteAndSortedMods()
    {
        var content = new InviteCardContent(
            "Survival", "Fabric 1.21.1", [("Radmin VPN", "26.1.2.3:25565"), ("Wi-Fi", "192.168.1.5:25565")], " peanut-net / 1234 ", ["Sodium", "Fabric API"]);

        var card = InviteCard.Compose(content, Labels);

        Assert.Equal(
            string.Join(Environment.NewLine,
                "Join Survival!", "Minecraft Fabric 1.21.1", "", "Address:", "• Radmin VPN: 26.1.2.3:25565", "• Wi-Fi: 192.168.1.5:25565",
                "", "Network: peanut-net / 1234", "", "Mods:", "• Fabric API", "• Sodium"),
            card);
    }

    [Fact]
    public void Compose_NoAddressNoteOrMods_LeavesThoseOut()
    {
        var card = InviteCard.Compose(new InviteCardContent("S", "Vanilla 26.3", [], "  ", []), Labels);

        Assert.Equal(string.Join(Environment.NewLine, "Join S!", "Minecraft Vanilla 26.3", "", "Address:", "• none yet"), card);
    }
}
