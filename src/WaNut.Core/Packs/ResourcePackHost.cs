using System.Net;
using System.Net.Sockets;
using System.Text;

namespace WaNut.Core.Packs;

/// <summary>
/// Serves one merged resource pack zip over plain HTTP while a server runs, because players' games download the pack
/// from the URL in server.properties. Only that file is served, whatever the request path. A tiny hand-written
/// responder, so no administrator rights or URL reservation are needed (HttpListener asks for them on Windows).
/// </summary>
public sealed class ResourcePackHost : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly string _zipPath;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    private ResourcePackHost(string zipPath)
    {
        _zipPath = zipPath;
        _listener = new TcpListener(IPAddress.Any, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _loop = Task.Run(AcceptLoopAsync);
    }

    /// <summary>The port the OS gave this host.</summary>
    public int Port { get; }

    public static ResourcePackHost Start(string zipPath) => new(zipPath);

    /// <summary>The address players' games download from, for <paramref name="address"/> (this PC's address friends can reach).</summary>
    public Uri UrlFor(IPAddress address) => new($"http://{address}:{Port}/resourcepack.zip");

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        _listener.Stop();
        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        _stop.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            _ = Task.Run(() => ServeAsync(client));
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using var _ = client;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            timeout.CancelAfter(TimeSpan.FromMinutes(10));
            var stream = client.GetStream();
            var method = await ReadMethodAsync(stream, timeout.Token).ConfigureAwait(false);
            if (method is not ("GET" or "HEAD"))
            {
                await WriteHeadAsync(stream, "405 Method Not Allowed", 0, timeout.Token).ConfigureAwait(false);
                return;
            }

            await using var file = new FileStream(_zipPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            await WriteHeadAsync(stream, "200 OK", file.Length, timeout.Token).ConfigureAwait(false);
            if (method == "GET")
            {
                await file.CopyToAsync(stream, timeout.Token).ConfigureAwait(false);
            }
        }
        catch (Exception e) when (e is IOException or SocketException or OperationCanceledException or ObjectDisposedException or UnauthorizedAccessException)
        {
            // A player's game hung up or the host stopped: nothing to tell anyone.
        }
    }

    /// <summary>Reads up to the blank line that ends the request head and returns the method of its first line.</summary>
    private static async Task<string> ReadMethodAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            length += read;
            if (Encoding.ASCII.GetString(buffer, 0, length).Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                break;
            }
        }

        var head = Encoding.ASCII.GetString(buffer, 0, length);
        var space = head.IndexOf(' ', StringComparison.Ordinal);
        return space > 0 ? head[..space] : string.Empty;
    }

    private static async Task WriteHeadAsync(NetworkStream stream, string status, long length, CancellationToken cancellationToken)
    {
        var head = $"HTTP/1.1 {status}\r\nContent-Type: application/zip\r\nContent-Length: {length}\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head), cancellationToken).ConfigureAwait(false);
    }
}
