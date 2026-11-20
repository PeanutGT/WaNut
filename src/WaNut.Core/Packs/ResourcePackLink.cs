using System.Security.Cryptography;

namespace WaNut.Core.Packs;

/// <summary>
/// A public download link the user typed for the merged resource pack, and what WaNut can tell about it: whether it is
/// a usable address, and (by downloading it) whether it serves the same zip WaNut built.
/// </summary>
public static class ResourcePackLink
{
    /// <summary>Turns what the user pasted into the address written to server.properties.</summary>
    /// <returns>Null when the text is not an http(s) address.</returns>
    public static Uri? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)
            || !Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || uri.Host.Length == 0)
        {
            return null;
        }

        return IsDropbox(uri) ? ForceDropboxDownload(uri) : uri;
    }

    /// <summary>The lower-case hex SHA-1 of a file, the form server.properties wants.</summary>
    public static string Sha1Of(string path)
    {
        using var file = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA1.HashData(file));
    }

    /// <summary>Downloads <paramref name="url"/> and compares it with the zip WaNut built.</summary>
    /// <param name="expectedSha1">The merged zip's SHA-1.</param>
    public static async Task<ResourcePackLinkCheck> CheckAsync(HttpClient http, Uri url, string expectedSha1, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new ResourcePackLinkCheck(ResourcePackLinkStatus.Unreachable, $"HTTP {(int)response.StatusCode}");
            }

            // A share page instead of the file: most sites do this when the link is not a direct download.
            if (response.Content.Headers.ContentType?.MediaType is "text/html" or "application/xhtml+xml")
            {
                return new ResourcePackLinkCheck(ResourcePackLinkStatus.WebPage);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
            var buffer = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                hash.AppendData(buffer, 0, read);
            }

            var actual = Convert.ToHexStringLower(hash.GetHashAndReset());
            return string.Equals(actual, expectedSha1, StringComparison.OrdinalIgnoreCase)
                ? new ResourcePackLinkCheck(ResourcePackLinkStatus.Match)
                : new ResourcePackLinkCheck(ResourcePackLinkStatus.Different);
        }
        catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return new ResourcePackLinkCheck(ResourcePackLinkStatus.Unreachable, e.Message);
        }
    }

    private static bool IsDropbox(Uri uri) =>
        uri.Host.Equals("dropbox.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".dropbox.com", StringComparison.OrdinalIgnoreCase);

    /// <summary>A Dropbox share link ends in dl=0 (a web page); dl=1 is the file itself.</summary>
    private static Uri ForceDropboxDownload(Uri uri)
    {
        var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(part => !part.StartsWith("dl=", StringComparison.OrdinalIgnoreCase))
            .Append("dl=1");
        return new UriBuilder(uri) { Query = string.Join('&', query) }.Uri;
    }
}
