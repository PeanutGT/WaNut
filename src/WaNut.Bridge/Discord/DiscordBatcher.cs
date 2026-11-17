using System.Text;

namespace WaNut.Bridge.Discord;

/// <summary>
/// Merges queued lines into as few Discord posts as the limits allow, so ten players joining at once is one request,
/// not ten. Order is kept: lines are only merged with their neighbours. Webhook lines merge only when they share a
/// name and picture, since one post shows one sender.
/// </summary>
public static class DiscordBatcher
{
    public const int MaxContentLength = 2000;

    public const int MaxEmbedsPerPost = 10;

    public const int MaxEmbedDescriptionLength = 4096;

    public const int MaxEmbedTextPerPost = 6000;

    public static IReadOnlyList<DiscordPost> Merge(IEnumerable<OutgoingMessage> messages)
    {
        var posts = new List<DiscordPost>();
        StringBuilder? content = null;
        string? username = null;
        string? avatarUrl = null;
        List<DiscordEmbed>? embeds = null;
        var embedText = 0;

        void Flush()
        {
            if (content is not null)
            {
                posts.Add(new DiscordPost { Content = content.ToString(), Username = username, AvatarUrl = avatarUrl });
                content = null;
            }

            if (embeds is not null)
            {
                posts.Add(new DiscordPost { Embeds = embeds });
                embeds = null;
                embedText = 0;
            }
        }

        foreach (var message in messages)
        {
            if (message.EmbedColor is { } color)
            {
                var description = Truncate(message.Text, MaxEmbedDescriptionLength);
                if (content is not null || embeds is { Count: MaxEmbedsPerPost } || embedText + description.Length > MaxEmbedTextPerPost)
                {
                    Flush();
                }

                embeds ??= [];
                embeds.Add(new DiscordEmbed(description, color));
                embedText += description.Length;
                continue;
            }

            var text = Truncate(message.Text, MaxContentLength);
            if (embeds is not null
                || (content is not null
                    && (username != message.Username || avatarUrl != message.AvatarUrl || content.Length + 1 + text.Length > MaxContentLength)))
            {
                Flush();
            }

            if (content is null)
            {
                content = new StringBuilder(text);
                username = message.Username;
                avatarUrl = message.AvatarUrl;
            }
            else
            {
                content.Append('\n').Append(text);
            }
        }

        Flush();
        return posts;
    }

    private static string Truncate(string text, int max)
    {
        if (text.Length <= max)
        {
            return text;
        }

        var cut = max - 1;
        if (char.IsHighSurrogate(text[cut - 1]))
        {
            cut--;
        }

        return text[..cut] + "…";
    }
}
