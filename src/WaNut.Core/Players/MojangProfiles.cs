using System.Net;
using System.Text.Json;

namespace WaNut.Core.Players;

/// <summary>Looks up a Minecraft account's UUID by player name, as servers with online-mode=true know players.</summary>
public sealed class MojangProfiles(HttpClient http)
{
    public static Uri ProfileUrl(string name) => new($"https://api.mojang.com/users/profiles/minecraft/{Uri.EscapeDataString(name)}");

    /// <summary>The account's UUID, or null when no account has that name.</summary>
    /// <exception cref="HttpRequestException">Mojang could not be reached or answered with an error.</exception>
    public async Task<Guid?> FindUuidAsync(string name, CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync(ProfileUrl(name), cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.NoContent)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            return json.RootElement.TryGetProperty("id", out var id) && Guid.TryParseExact(id.GetString(), "N", out var uuid) ? uuid : null;
        }
        catch (JsonException e)
        {
            throw new HttpRequestException("Mojang's reply was not a profile.", e);
        }
    }
}
