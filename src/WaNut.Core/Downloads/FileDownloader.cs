namespace WaNut.Core.Downloads;

/// <summary>
/// Downloads files over HTTPS and only keeps them when they match their published checksum.
/// </summary>
public sealed class FileDownloader(HttpClient http)
{
    private const int BufferSize = 81920;

    public async Task DownloadAsync(
        Uri url,
        string destination,
        ExpectedHash expectedHash,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        RequireHttps(url);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        var partial = destination + ".part";

        try
        {
            using (var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength;

                using var hasher = expectedHash.CreateHasher();
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using (var target = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
                {
                    var buffer = new byte[BufferSize];
                    long received = 0;
                    int read;
                    while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        hasher.AppendData(buffer, 0, read);
                        await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                        received += read;
                        progress?.Report(new DownloadProgress(received, total));
                    }
                }

                if (!expectedHash.Matches(hasher.GetHashAndReset()))
                {
                    throw new DownloadVerificationException(url, expectedHash);
                }
            }

            File.Move(partial, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(partial))
            {
                File.Delete(partial);
            }
        }
    }

    /// <summary>
    /// Downloads a small metadata document into memory and checks it against a known checksum.
    /// </summary>
    public async Task<byte[]> DownloadBytesAsync(Uri url, ExpectedHash expectedHash, CancellationToken cancellationToken = default)
    {
        RequireHttps(url);

        var bytes = await http.GetByteArrayAsync(url, cancellationToken).ConfigureAwait(false);
        using var hasher = expectedHash.CreateHasher();
        hasher.AppendData(bytes);
        if (!expectedHash.Matches(hasher.GetHashAndReset()))
        {
            throw new DownloadVerificationException(url, expectedHash);
        }

        return bytes;
    }

    private static void RequireHttps(Uri url)
    {
        if (url.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException($"Only HTTPS downloads are allowed: {url}", nameof(url));
        }
    }
}
