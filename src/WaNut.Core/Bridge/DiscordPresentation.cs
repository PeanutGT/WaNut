namespace WaNut.Core.Bridge;

/// <summary>How Minecraft messages look in the Discord channel.</summary>
public enum DiscordPresentation
{
    /// <summary>A webhook posts as the player, with their name and head as the avatar.</summary>
    Webhook,

    /// <summary>The bot posts colored embed cards.</summary>
    Embed,

    /// <summary>The bot posts plain "[MC] name: text" lines.</summary>
    PlainText,
}
