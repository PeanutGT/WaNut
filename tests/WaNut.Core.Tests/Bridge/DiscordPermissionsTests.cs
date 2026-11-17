using WaNut.Core.Bridge;

namespace WaNut.Core.Tests.Bridge;

public sealed class DiscordPermissionsTests
{
    private const string Guild = "1";
    private const string Bot = "2";
    private const string BotRole = "3";
    private const long Everyone = DiscordPermissions.ViewChannel | DiscordPermissions.SendMessages;

    private static readonly Dictionary<string, long> Roles = new() { [Guild] = Everyone, [BotRole] = DiscordPermissions.EmbedLinks };

    [Fact]
    public void InChannel_NoOverwrites_IsEveryonePlusRoles()
    {
        var permissions = DiscordPermissions.InChannel(Bot, Guild, "9", Roles, [BotRole], []);

        Assert.Equal(Everyone | DiscordPermissions.EmbedLinks, permissions);
    }

    [Fact]
    public void InChannel_EveryoneDeniedButRoleAllowed_RoleWins()
    {
        DiscordOverwrite[] overwrites =
        [
            new(Guild, false, 0, DiscordPermissions.ViewChannel),
            new(BotRole, false, DiscordPermissions.ViewChannel, 0),
        ];

        var permissions = DiscordPermissions.InChannel(Bot, Guild, "9", Roles, [BotRole], overwrites);

        Assert.NotEqual(0, permissions & DiscordPermissions.ViewChannel);
    }

    [Fact]
    public void InChannel_MemberOverwrite_ComesLast()
    {
        DiscordOverwrite[] overwrites =
        [
            new(BotRole, false, DiscordPermissions.SendMessages, 0),
            new(Bot, true, 0, DiscordPermissions.SendMessages),
        ];

        var permissions = DiscordPermissions.InChannel(Bot, Guild, "9", Roles, [BotRole], overwrites);

        Assert.Equal(0, permissions & DiscordPermissions.SendMessages);
    }

    [Fact]
    public void InChannel_Administrator_IgnoresOverwrites()
    {
        var roles = new Dictionary<string, long>(Roles) { [BotRole] = DiscordPermissions.Administrator };

        var permissions = DiscordPermissions.InChannel(Bot, Guild, "9", roles, [BotRole], [new(Bot, true, 0, DiscordPermissions.ViewChannel)]);

        Assert.Equal(DiscordPermissions.All, permissions);
    }

    [Fact]
    public void Missing_EmbedWithoutEmbedLinks_NamesIt()
    {
        var needed = DiscordPermissions.Needed(new BridgeConfig { Presentation = DiscordPresentation.Embed });

        Assert.Equal(["Embed Links"], DiscordPermissions.Missing(Everyone, needed));
    }

    [Fact]
    public void Needed_Webhook_OnlyViewChannel()
    {
        Assert.Equal(DiscordPermissions.ViewChannel, DiscordPermissions.Needed(new BridgeConfig { Presentation = DiscordPresentation.Webhook }));
    }
}
