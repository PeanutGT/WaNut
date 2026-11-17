namespace WaNut.Bridge.Discord;

public interface IDiscordPoster
{
    Task<DiscordPostResult> PostAsync(DiscordPost post, CancellationToken cancellationToken);
}
