using System.Runtime.CompilerServices;
using System.Text;

namespace WaNut.Bridge.GameLog;

/// <summary>
/// Follows a growing log file the way <c>tail -F</c> does, without ever locking it: the server keeps writing,
/// renaming and deleting it as usual. When the server starts again it replaces <c>latest.log</c>; that is seen
/// either because the file got shorter than what was read or because its first bytes changed, and reading
/// restarts at the top of the new file.
/// </summary>
public sealed class LogTailReader(string path)
{
    /// <summary>Bytes from the start of the file that identify it. The first line holds the start time.</summary>
    public const int FingerprintLength = 128;

    /// <summary>The most one poll reads, so a huge file never fills memory at once; the rest comes next poll.</summary>
    public const int MaxBytesPerPoll = 4 * 1024 * 1024;

    /// <summary>
    /// The longest line kept, in bytes; the rest of a longer one is dropped. Chat and server messages are far shorter,
    /// so only a runaway line (a mod dumping a huge object without line breaks) is cut, and it cannot fill memory.
    /// </summary>
    public const int MaxLineLength = 16 * 1024;

    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(1);

    private readonly List<byte> _partialLine = [];
    private byte[]? _fingerprint;
    private long _offset;

    public string Path { get; } = path;

    /// <summary>Lines are delivered from the end of the file as it is now; older lines are history, not news.</summary>
    public async IAsyncEnumerable<string> ReadLinesAsync(
        TimeSpan? pollInterval = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await SkipToEndAsync(cancellationToken).ConfigureAwait(false);
        while (true)
        {
            foreach (var line in await PollAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return line;
            }

            await Task.Delay(pollInterval ?? DefaultPollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Marks everything in the file now as read. A missing file is read from its first byte once it appears.</summary>
    public async Task SkipToEndAsync(CancellationToken cancellationToken = default)
    {
        await using var stream = TryOpen();
        if (stream is null)
        {
            _fingerprint = [];
            _offset = 0;
            return;
        }

        _fingerprint = await ReadFingerprintAsync(stream, cancellationToken).ConfigureAwait(false);
        _offset = stream.Length;
        _partialLine.Clear();
    }

    /// <summary>The complete lines written since the last poll. A line without its newline yet waits for the next poll.</summary>
    public async Task<IReadOnlyList<string>> PollAsync(CancellationToken cancellationToken = default)
    {
        await using var stream = TryOpen();
        if (stream is null)
        {
            return [];
        }

        var fingerprint = await ReadFingerprintAsync(stream, cancellationToken).ConfigureAwait(false);
        if (IsNewFile(fingerprint, stream.Length))
        {
            _offset = 0;
            _partialLine.Clear();
        }

        _fingerprint = fingerprint;
        if (stream.Length == _offset)
        {
            return [];
        }

        stream.Position = _offset;
        var toRead = (int)Math.Min(stream.Length - _offset, MaxBytesPerPoll);
        var buffer = new byte[toRead];
        var read = 0;
        while (read < toRead)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read, toRead - read), cancellationToken).ConfigureAwait(false);
            if (n == 0)
            {
                break;
            }

            read += n;
        }

        _offset += read;
        return SplitLines(buffer.AsSpan(0, read));
    }

    private bool IsNewFile(byte[] fingerprint, long length)
    {
        if (_fingerprint is null)
        {
            return true;
        }

        // The stored head is shorter while the file is young; it only has to be a prefix of the current head.
        return length < _offset
            || fingerprint.Length < _fingerprint.Length
            || !fingerprint.AsSpan(0, _fingerprint.Length).SequenceEqual(_fingerprint);
    }

    private List<string> SplitLines(ReadOnlySpan<byte> bytes)
    {
        var lines = new List<string>();
        while (true)
        {
            var newline = bytes.IndexOf((byte)'\n');
            if (newline < 0)
            {
                AppendPartial(bytes);
                return lines;
            }

            string line;
            if (_partialLine.Count > 0)
            {
                AppendPartial(bytes[..newline]);
                line = Encoding.UTF8.GetString([.. _partialLine]);
                _partialLine.Clear();
            }
            else
            {
                line = Encoding.UTF8.GetString(bytes[..Math.Min(newline, MaxLineLength)]);
            }

            lines.Add(line.TrimEnd('\r'));
            bytes = bytes[(newline + 1)..];
        }
    }

    private void AppendPartial(ReadOnlySpan<byte> bytes)
    {
        var room = MaxLineLength - _partialLine.Count;
        if (room > 0)
        {
            _partialLine.AddRange(bytes[..Math.Min(room, bytes.Length)]);
        }
    }

    private FileStream? TryOpen()
    {
        try
        {
            // Delete lets the server rename or remove the file while it is open here (it does so on every start).
            return new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, useAsync: true);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    private static async Task<byte[]> ReadFingerprintAsync(FileStream stream, CancellationToken cancellationToken)
    {
        var head = new byte[(int)Math.Min(stream.Length, FingerprintLength)];
        stream.Position = 0;
        var read = 0;
        while (read < head.Length)
        {
            var n = await stream.ReadAsync(head.AsMemory(read), cancellationToken).ConfigureAwait(false);
            if (n == 0)
            {
                break;
            }

            read += n;
        }

        return read == head.Length ? head : head[..read];
    }
}
