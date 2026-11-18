using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using WaNut.Core.Http;

namespace WaNut.Bridge.Discord;

/// <summary>The bot's REST calls besides posting: slash command registration and answers. 429s are waited out.</summary>
public sealed class DiscordRest(HttpClient http, string botToken, Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    /// <summary>"Thinking…", answered later by <see cref="EditReplyAsync"/>; RCON may take longer than the 3 seconds Discord allows.</summary>
    private const int DeferredReply = 5;

    /// <summary>Only the person who used the command sees the answer.</summary>
    private const int EphemeralFlag = 64;

    /// <exception cref="HttpRequestException">Discord refused, for example because the bot cannot see the channel.</exception>
    public async Task<string> GetGuildIdAsync(ulong channelId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, $"channels/{channelId.ToString(CultureInfo.InvariantCulture)}", null, true, cancellationToken).ConfigureAwait(false);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        return json.RootElement.GetProperty("guild_id").GetString() ?? throw new HttpRequestException("The channel is not in a server.");
    }

    /// <summary>Makes the server's commands for this bot exactly <paramref name="commandsJson"/>; repeating it changes nothing.</summary>
    public async Task RegisterCommandsAsync(string applicationId, string guildId, string commandsJson, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Put, $"applications/{applicationId}/guilds/{guildId}/commands", commandsJson, true, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeferReplyAsync(SlashCommand command, bool ephemeral, CancellationToken cancellationToken)
    {
        var body = JsonText.Write(json =>
        {
            json.WriteStartObject();
            json.WriteNumber("type", DeferredReply);
            json.WriteStartObject("data");
            json.WriteNumber("flags", ephemeral ? EphemeralFlag : 0);
            json.WriteEndObject();
            json.WriteEndObject();
        });
        using var response = await SendAsync(HttpMethod.Post, $"interactions/{command.InteractionId}/{command.Token}/callback", body, false, cancellationToken).ConfigureAwait(false);
    }

    public async Task EditReplyAsync(SlashCommand command, string content, CancellationToken cancellationToken)
    {
        var body = JsonText.Write(json =>
        {
            json.WriteStartObject();
            json.WriteString("content", content.Length > DiscordBatcher.MaxContentLength ? content[..(DiscordBatcher.MaxContentLength - 1)] + "…" : content);
            JsonText.WriteNoMentions(json);
            json.WriteEndObject();
        });
        using var response = await SendAsync(HttpMethod.Patch, $"webhooks/{command.ApplicationId}/{command.Token}/messages/@original", body, false, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Posts <paramref name="body"/> (a message JSON) to the channel and returns the new message's id.</summary>
    public async Task<string> PostMessageAsync(ulong channelId, string body, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post, $"channels/{channelId.ToString(CultureInfo.InvariantCulture)}/messages", body, true, cancellationToken).ConfigureAwait(false);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        return json.RootElement.GetProperty("id").GetString() ?? throw new HttpRequestException("Discord sent no message id.");
    }

    /// <exception cref="HttpRequestException">With status 404 when the message was deleted.</exception>
    public async Task EditMessageAsync(ulong channelId, string messageId, string body, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Patch, $"channels/{channelId.ToString(CultureInfo.InvariantCulture)}/messages/{messageId}", body, true, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? body, bool authorize, CancellationToken cancellationToken)
    {
        var response = await RateLimitRetry.SendAsync(
            http,
            () =>
            {
                var request = new HttpRequestMessage(method, DiscordHttpPoster.ApiBase + path);
                if (body is not null)
                {
                    request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                }

                // Interaction answers are authorized by their token in the URL.
                if (authorize)
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bot", botToken);
                }

                return request;
            },
            delay,
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            response.Dispose();
            throw new HttpRequestException($"Discord answered HTTP {status} to {method} {Redact(path)}.", null, (System.Net.HttpStatusCode)status);
        }

        return response;
    }

    // Interaction tokens are secrets for 15 minutes; keep them out of the log.
    private static string Redact(string path) => path.StartsWith("interactions/", StringComparison.Ordinal) || path.StartsWith("webhooks/", StringComparison.Ordinal)
        ? path[..path.IndexOf('/', StringComparison.Ordinal)] + "/…"
        : path;
}
