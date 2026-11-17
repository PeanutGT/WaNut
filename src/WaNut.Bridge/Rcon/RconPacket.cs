using System.Buffers.Binary;
using System.Text;

namespace WaNut.Bridge.Rcon;

/// <summary>
/// One Source RCON packet as Minecraft speaks it: a little-endian length, request id and type, then the body and two
/// zero bytes. The length counts everything after itself.
/// </summary>
public sealed record RconPacket(int RequestId, int Type, string Body)
{
    public const int TypeLogin = 3;

    public const int TypeCommand = 2;

    /// <summary>The server answers a login with this type too; a failed login has request id -1.</summary>
    public const int TypeLoginResponse = 2;

    public const int TypeResponse = 0;

    /// <summary>Minecraft drops a request whose body is longer than this many bytes.</summary>
    public const int MaxRequestBodyBytes = 1446;

    /// <summary>Larger than any answer Minecraft sends (it splits at 4096 bytes), so a bad length cannot eat memory.</summary>
    public const int MaxPacketLength = 64 * 1024;

    private const int HeaderLength = 8;

    public byte[] Encode()
    {
        var body = Encoding.UTF8.GetBytes(Body);
        var packet = new byte[4 + HeaderLength + body.Length + 2];
        BinaryPrimitives.WriteInt32LittleEndian(packet, HeaderLength + body.Length + 2);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(4), RequestId);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(8), Type);
        body.CopyTo(packet, 12);
        return packet;
    }

    /// <exception cref="EndOfStreamException">The server closed the connection.</exception>
    /// <exception cref="InvalidDataException">The bytes are not an RCON packet.</exception>
    public static async Task<RconPacket> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var lengthBytes = new byte[4];
        await stream.ReadExactlyAsync(lengthBytes, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
        if (length < HeaderLength + 2 || length > MaxPacketLength)
        {
            throw new InvalidDataException($"RCON packet length {length} is out of range.");
        }

        var rest = new byte[length];
        await stream.ReadExactlyAsync(rest, cancellationToken).ConfigureAwait(false);
        var requestId = BinaryPrimitives.ReadInt32LittleEndian(rest);
        var type = BinaryPrimitives.ReadInt32LittleEndian(rest.AsSpan(4));
        var body = rest.AsSpan(HeaderLength, length - HeaderLength);
        var end = body.IndexOf((byte)0);
        return new RconPacket(requestId, type, Encoding.UTF8.GetString(end < 0 ? body : body[..end]));
    }
}
