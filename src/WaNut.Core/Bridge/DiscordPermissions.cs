namespace WaNut.Core.Bridge;

/// <summary>Discord's permission bits and its documented order for working out a member's permissions in a channel.</summary>
public static class DiscordPermissions
{
    public const long Administrator = 1L << 3;
    public const long ViewChannel = 1L << 10;
    public const long SendMessages = 1L << 11;
    public const long EmbedLinks = 1L << 14;

    public const long All = long.MaxValue;

    /// <summary>
    /// The guild's @everyone role (its id is the guild id), plus the member's roles, then the channel's overwrites:
    /// @everyone, all role denies, all role allows, then the member's own. Owners and administrators have everything.
    /// </summary>
    public static long InChannel(
        string memberId,
        string guildId,
        string? ownerId,
        IReadOnlyDictionary<string, long> guildRoles,
        IReadOnlyCollection<string> memberRoles,
        IReadOnlyCollection<DiscordOverwrite> overwrites)
    {
        if (memberId == ownerId)
        {
            return All;
        }

        var permissions = guildRoles.GetValueOrDefault(guildId);
        foreach (var role in memberRoles)
        {
            permissions |= guildRoles.GetValueOrDefault(role);
        }

        if ((permissions & Administrator) != 0)
        {
            return All;
        }

        if (overwrites.FirstOrDefault(o => !o.IsMember && o.Id == guildId) is { } everyone)
        {
            permissions = (permissions & ~everyone.Deny) | everyone.Allow;
        }

        long allow = 0, deny = 0;
        foreach (var overwrite in overwrites.Where(o => !o.IsMember && o.Id != guildId && memberRoles.Contains(o.Id)))
        {
            allow |= overwrite.Allow;
            deny |= overwrite.Deny;
        }

        permissions = (permissions & ~deny) | allow;
        if (overwrites.FirstOrDefault(o => o.IsMember && o.Id == memberId) is { } own)
        {
            permissions = (permissions & ~own.Deny) | own.Allow;
        }

        return permissions;
    }

    /// <summary>What the bot needs in the channel for <paramref name="config"/>: always to see it, and to post when it posts.</summary>
    public static long Needed(BridgeConfig config) =>
        ViewChannel
        | (config.Presentation == DiscordPresentation.Webhook ? 0 : SendMessages)
        | (config.Presentation == DiscordPresentation.Embed ? EmbedLinks : 0);

    /// <summary>The names, as Discord shows them in English, of the bits in <paramref name="needed"/> that are missing.</summary>
    public static IReadOnlyList<string> Missing(long permissions, long needed)
    {
        var missing = new List<string>();
        void Check(long bit, string name)
        {
            if ((needed & bit) != 0 && (permissions & bit) == 0)
            {
                missing.Add(name);
            }
        }

        Check(ViewChannel, "View Channel");
        Check(SendMessages, "Send Messages");
        Check(EmbedLinks, "Embed Links");
        return missing;
    }
}
