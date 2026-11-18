using System.Buffers.Binary;
using System.Globalization;
using System.IO.Pipes;
using System.Text;

namespace WaNut.Core.Presence;

/// <summary>
/// A small client for the Discord desktop app's local IPC, enough for Rich Presence: connect, handshake with the
/// application id, set or clear the activity. Frames are a 4-byte opcode, a 4-byte length and JSON. Every failure
/// (Discord closed, pipe gone) is reported as false so the caller can try again later; nothing here may disturb a server.
/// </summary>
public sealed class DiscordIpcClient(string applicationId, Func<int, CancellationToken, Task<Stream?>>? connect = null) : IDisposable
{
    private const int Handshake = 0;
    private const int Frame = 1;
    private const int MaxFrameLength = 64 * 1024;

    private readonly Func<int, CancellationToken, Task<Stream?>> _connect = connect ?? ConnectPipeAsync;
    private Stream? _stream;

    public bool IsConnected => _stream is not null;

    /// <summary>Sets the activity, or clears it when null. Connects first if needed.</summary>
    public async Task<bool> SetActivityAsync(PresenceActivity? activity, CancellationToken cancellationToken)
    {
        try
        {
            if (_stream is null && !await OpenAsync(cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            var command = PresenceActivity.BuildCommand(activity, Environment.ProcessId, Guid.NewGuid().ToString("N"));
            await WriteFrameAsync(_stream!, Frame, command, cancellationToken).ConfigureAwait(false);
            await ReadFrameAsync(_stream!, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidDataException or UnauthorizedAccessException)
        {
            Close();
            return false;
        }
    }

    public void Dispose() => Close();

    private async Task<bool> OpenAsync(CancellationToken cancellationToken)
    {
        // The app listens on discord-ipc-0, or the next number when several Discord builds run.
        for (var i = 0; i < 10; i++)
        {
            var stream = await _connect(i, cancellationToken).ConfigureAwait(false);
            if (stream is null)
            {
                continue;
            }

            try
            {
                await WriteFrameAsync(stream, Handshake, $$"""{"v":1,"client_id":"{{applicationId}}"}""", cancellationToken).ConfigureAwait(false);
                await ReadFrameAsync(stream, cancellationToken).ConfigureAwait(false);
                _stream = stream;
                return true;
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidDataException)
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }
        }

        return false;
    }

    private void Close()
    {
        _stream?.Dispose();
        _stream = null;
    }

    private static async Task<Stream?> ConnectPipeAsync(int index, CancellationToken cancellationToken)
    {
        var pipe = new NamedPipeClientStream(".", "discord-ipc-" + index.ToString(CultureInfo.InvariantCulture), PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(300));
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            return pipe;
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or UnauthorizedAccessException or TimeoutException)
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
    }

    internal static async Task WriteFrameAsync(Stream stream, int opcode, string json, CancellationToken cancellationToken)
    {
        var body = Encoding.UTF8.GetBytes(json);
        var frame = new byte[8 + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, opcode);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), body.Length);
        body.CopyTo(frame, 8);
        await stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<string> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[8];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
        if (length is < 0 or > MaxFrameLength)
        {
            throw new InvalidDataException("Discord sent a frame of an unexpected size.");
        }

        var body = new byte[length];
        await stream.ReadExactlyAsync(body, cancellationToken).ConfigureAwait(false);
        return Encoding.UTF8.GetString(body);
    }
}
