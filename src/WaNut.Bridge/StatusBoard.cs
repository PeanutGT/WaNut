using System.Globalization;
using System.Net;
using System.Text.Json;
using WaNut.Bridge.Discord;
using WaNut.Bridge.Relay;

namespace WaNut.Bridge;

/// <summary>How <see cref="StatusBoard.PlaceAsync"/> ended.</summary>
public enum StatusPlacement
{
    /// <summary>A new message was posted.</summary>
    Created,

    /// <summary>The board was already in that channel; it was refreshed.</summary>
    AlreadyHere,

    /// <summary>The board was in another channel; that message was removed and a new one posted here.</summary>
    Moved,
}

/// <summary>
/// Keeps one message in the server (guild) up to date with players, TPS and uptime by editing it every
/// <see cref="Interval"/>. A bridge serves one guild, so one board is all there can be: <see cref="PlaceAsync"/>
/// (the /status command) creates it or moves it to another channel. Where it lives is kept in a file so a restart
/// edits the same message. A deleted message is posted again; a deleted channel makes the board forget its place until
/// the next /status. Needs the bot (webhook-only bridges cannot edit).
/// </summary>
public sealed class StatusBoard(DiscordRest rest, ulong? defaultChannelId, BridgeRelay relay, bool chinese, string stateFile, BridgeLog log)
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _refresh = new(0, 1);
    private (ulong ChannelId, string? MessageId)? _place;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        // Without a remembered place, the bridge channel gets the board when the setting asks for it (defaultChannelId).
        _place = ReadPlace() ?? (defaultChannelId is { } channel ? (channel, null) : null);
        while (!cancellationToken.IsCancellationRequested)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await UpdateAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                log.Warn($"Could not update the status board: {ex.Message}");
            }
            finally
            {
                _gate.Release();
            }

            await _refresh.WaitAsync(Interval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Puts the board in <paramref name="channelId"/>: the one board of this server, wherever it was.</summary>
    /// <exception cref="HttpRequestException">The bot cannot post in that channel.</exception>
    public async Task<StatusPlacement> PlaceAsync(ulong channelId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var previous = _place;
            var body = await BuildAsync(cancellationToken).ConfigureAwait(false);
            if (previous is { MessageId: { } id } here && here.ChannelId == channelId)
            {
                try
                {
                    await rest.EditMessageAsync(channelId, id, body, cancellationToken).ConfigureAwait(false);
                    return StatusPlacement.AlreadyHere;
                }
                catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                {
                    log.Info("The status board message is gone; posting a new one.");
                }
            }

            var created = await rest.PostMessageAsync(channelId, body, cancellationToken).ConfigureAwait(false);
            _place = (channelId, created);
            WritePlace();
            if (previous is { MessageId: { } old } other && other.ChannelId != channelId)
            {
                await RemoveAsync(other.ChannelId, old, cancellationToken).ConfigureAwait(false);
                return StatusPlacement.Moved;
            }

            return StatusPlacement.Created;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task UpdateAsync(CancellationToken cancellationToken)
    {
        if (_place is not { } place)
        {
            return;
        }

        var body = await BuildAsync(cancellationToken).ConfigureAwait(false);
        if (place.MessageId is not null)
        {
            try
            {
                await rest.EditMessageAsync(place.ChannelId, place.MessageId, body, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                log.Info("The status board message is gone; posting a new one.");
            }
        }

        try
        {
            _place = (place.ChannelId, await rest.PostMessageAsync(place.ChannelId, body, cancellationToken).ConfigureAwait(false));
            WritePlace();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // The channel itself is gone: stop trying until someone runs /status again.
            log.Warn("The status board channel is gone. Use /status in a channel to put the board there.");
            _place = null;
            WritePlace();
        }
    }

    private async Task<string> BuildAsync(CancellationToken cancellationToken) =>
        StatusBoardMessage.Build(await relay.CollectBoardAsync(cancellationToken).ConfigureAwait(false), chinese, DateTimeOffset.UtcNow);

    private async Task RemoveAsync(ulong channelId, string messageId, CancellationToken cancellationToken)
    {
        try
        {
            await rest.DeleteMessageAsync(channelId, messageId, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            // Already gone, or the bot lost access to the old channel; the new board is what counts.
            log.Warn($"Could not remove the old status board message: {ex.Message}");
        }
    }

    private (ulong, string?)? ReadPlace()
    {
        try
        {
            if (File.Exists(stateFile))
            {
                using var json = JsonDocument.Parse(File.ReadAllText(stateFile));
                var root = json.RootElement;
                if (root.TryGetProperty("channelId", out var c) && ulong.TryParse(c.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var channel))
                {
                    var message = root.TryGetProperty("messageId", out var m) ? m.GetString() : null;
                    return (channel, message is { Length: > 0 } && message.All(char.IsAsciiDigit) ? message : null);
                }

                return null;
            }

            // Before /status the file held only the message id, always in the bridge channel.
            var legacy = Path.Combine(Path.GetDirectoryName(stateFile) ?? string.Empty, "status-board.id");
            var text = File.Exists(legacy) ? File.ReadAllText(legacy).Trim() : string.Empty;
            return defaultChannelId is { } fallback && text.Length > 0 && text.All(char.IsAsciiDigit) ? (fallback, text) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private void WritePlace()
    {
        try
        {
            if (_place is not { } place)
            {
                File.Delete(stateFile);
                return;
            }

            var json = JsonText.Write(w =>
            {
                w.WriteStartObject();
                w.WriteString("channelId", place.ChannelId.ToString(CultureInfo.InvariantCulture));
                w.WriteString("messageId", place.MessageId);
                w.WriteEndObject();
            });
            File.WriteAllText(stateFile, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Warn($"Could not remember the status board message: {ex.Message}");
        }
    }
}
