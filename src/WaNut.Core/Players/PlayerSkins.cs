using System.Text;
using System.Text.Json;

namespace WaNut.Core.Players;

/// <summary>
/// Downloads players' skins from Mojang, for the faces on the Players tab, and keeps them in the cache folder for a
/// day so the tab does not ask Mojang again every time it opens. Players without an account or a custom skin get
/// none, and the tab shows a default face.
/// </summary>
public sealed class PlayerSkins(HttpClient http, MojangProfiles profiles, LauncherPaths paths, TimeProvider time)
{
    public static readonly TimeSpan CacheLife = TimeSpan.FromDays(1);

    /// <summary>Mojang's own HttpClient timeout is long, for big downloads; a face is not worth waiting for.</summary>
    private static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(15);

    private const string TexturesHost = "textures.minecraft.net";

    // Marks a name looked up recently that has no skin, so it is not looked up again until the cache expires.
    private const string NoSkinExtension = ".none";

    public string SkinsDir => Path.Combine(paths.CacheDir, "skins");

    public static Uri SessionProfileUrl(Guid uuid) => new($"https://sessionserver.mojang.com/session/minecraft/profile/{uuid:N}");

    /// <summary>
    /// The path of the player's skin PNG, downloading it when the cached copy is missing or older than
    /// <see cref="CacheLife"/>; null when the player has no custom skin or none could be found. When Mojang cannot
    /// be reached, an older cached skin is still used.
    /// </summary>
    public async Task<string?> GetSkinFileAsync(string name, CancellationToken cancellationToken = default)
    {
        if (!PlayerLists.IsValidName(name))
        {
            return null;
        }

        var file = Path.Combine(SkinsDir, name.ToLowerInvariant() + ".png");
        var noSkin = Path.ChangeExtension(file, NoSkinExtension);
        if (IsFresh(file))
        {
            return file;
        }

        if (IsFresh(noSkin))
        {
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(LookupTimeout);
        try
        {
            var bytes = await DownloadSkinAsync(name, timeout.Token).ConfigureAwait(false);
            Directory.CreateDirectory(SkinsDir);
            if (bytes is null)
            {
                await File.WriteAllTextAsync(noSkin, string.Empty, CancellationToken.None).ConfigureAwait(false);
                File.Delete(file);
                return null;
            }

            var temp = file + ".tmp";
            await File.WriteAllBytesAsync(temp, bytes, CancellationToken.None).ConfigureAwait(false);
            File.Move(temp, file, overwrite: true);
            File.Delete(noSkin);
            return file;
        }
        catch (Exception e) when (e is HttpRequestException or IOException or UnauthorizedAccessException
            || (e is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return File.Exists(file) ? file : null;
        }
    }

    /// <summary>The skin's address from a session profile, or null when the player uses a default skin.</summary>
    public static Uri? ParseSkinUrl(string profileJson)
    {
        try
        {
            using var profile = JsonDocument.Parse(profileJson);
            if (!profile.RootElement.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var property in properties.EnumerateArray())
            {
                if (property.TryGetProperty("name", out var name) && name.GetString() == "textures"
                    && property.TryGetProperty("value", out var value) && value.GetString() is { } encoded)
                {
                    using var textures = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)));
                    if (textures.RootElement.TryGetProperty("textures", out var all)
                        && all.TryGetProperty("SKIN", out var skin)
                        && skin.TryGetProperty("url", out var url)
                        && Uri.TryCreate(url.GetString(), UriKind.Absolute, out var uri)
                        && uri.Host.Equals(TexturesHost, StringComparison.OrdinalIgnoreCase))
                    {
                        // Mojang hands out http:// addresses; the same files are served over HTTPS.
                        return new UriBuilder(uri) { Scheme = Uri.UriSchemeHttps, Port = -1 }.Uri;
                    }
                }
            }

            return null;
        }
        catch (Exception e) when (e is JsonException or FormatException or InvalidOperationException)
        {
            return null;
        }
    }

    private async Task<byte[]?> DownloadSkinAsync(string name, CancellationToken cancellationToken)
    {
        if (await profiles.FindUuidAsync(name, cancellationToken).ConfigureAwait(false) is not { } uuid)
        {
            return null;
        }

        using var response = await http.GetAsync(SessionProfileUrl(uuid), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var profile = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (ParseSkinUrl(profile) is not { } skinUrl)
        {
            return null;
        }

        return await http.GetByteArrayAsync(skinUrl, cancellationToken).ConfigureAwait(false);
    }

    private bool IsFresh(string path) =>
        File.Exists(path) && time.GetUtcNow() - File.GetLastWriteTimeUtc(path) < CacheLife;
}
