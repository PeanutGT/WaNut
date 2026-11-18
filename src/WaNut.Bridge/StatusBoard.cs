using System.Net;
using WaNut.Bridge.Discord;
using WaNut.Bridge.Relay;

namespace WaNut.Bridge;

/// <summary>
/// Keeps one message in the bridge channel up to date with players, TPS and uptime by editing it every
/// <see cref="Interval"/>. The message id is kept in a file so a restart edits the same message; if someone deleted it,
/// a new one is posted. Needs the bot (webhook-only bridges cannot edit).
/// </summary>
public sealed class StatusBoard(DiscordRest rest, ulong channelId, BridgeRelay relay, bool chinese, string idFile, BridgeLog log)
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var messageId = ReadId();
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var body = StatusBoardMessage.Build(await relay.CollectBoardAsync(cancellationToken).ConfigureAwait(false), chinese, DateTimeOffset.UtcNow);
                messageId = await PublishAsync(messageId, body, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                log.Warn($"Could not update the status board: {ex.Message}");
            }

            await Task.Delay(Interval, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<string> PublishAsync(string? messageId, string body, CancellationToken cancellationToken)
    {
        if (messageId is not null)
        {
            try
            {
                await rest.EditMessageAsync(channelId, messageId, body, cancellationToken).ConfigureAwait(false);
                return messageId;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                log.Info("The status board message is gone; posting a new one.");
            }
        }

        var created = await rest.PostMessageAsync(channelId, body, cancellationToken).ConfigureAwait(false);
        WriteId(created);
        return created;
    }

    private string? ReadId()
    {
        try
        {
            var text = File.Exists(idFile) ? File.ReadAllText(idFile).Trim() : string.Empty;
            return text.Length > 0 && text.All(char.IsAsciiDigit) ? text : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private void WriteId(string id)
    {
        try
        {
            File.WriteAllText(idFile, id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Warn($"Could not remember the status board message: {ex.Message}");
        }
    }
}
