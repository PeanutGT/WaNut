using WaNut.Bridge.Rcon;

namespace WaNut.Bridge.Tests.Rcon;

public sealed class RconPacketTests
{
    [Fact]
    public void Encode_Command_MatchesTheWireFormat()
    {
        var bytes = new RconPacket(7, RconPacket.TypeCommand, "list").Encode();

        byte[] expected = [14, 0, 0, 0, 7, 0, 0, 0, 2, 0, 0, 0, (byte)'l', (byte)'i', (byte)'s', (byte)'t', 0, 0];
        Assert.Equal(expected, bytes);
    }

    [Fact]
    public async Task ReadAsync_EncodedPacket_RoundTrips()
    {
        var packet = new RconPacket(-1, RconPacket.TypeResponse, "Ünïcødé ok");
        using var stream = new MemoryStream(packet.Encode());

        Assert.Equal(packet, await RconPacket.ReadAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task ReadAsync_TwoPacketsBackToBack_ReadsOneAtATime()
    {
        using var stream = new MemoryStream([.. new RconPacket(1, 0, "a").Encode(), .. new RconPacket(2, 0, "b").Encode()]);

        Assert.Equal("a", (await RconPacket.ReadAsync(stream, CancellationToken.None)).Body);
        Assert.Equal("b", (await RconPacket.ReadAsync(stream, CancellationToken.None)).Body);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(RconPacket.MaxPacketLength + 1)]
    public async Task ReadAsync_LengthOutOfRange_Throws(int length)
    {
        using var stream = new MemoryStream([.. BitConverter.GetBytes(length), .. new byte[16]]);

        await Assert.ThrowsAsync<InvalidDataException>(() => RconPacket.ReadAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task ReadAsync_ConnectionClosedMidPacket_Throws()
    {
        using var stream = new MemoryStream(new RconPacket(1, 0, "abc").Encode()[..9]);

        await Assert.ThrowsAsync<EndOfStreamException>(() => RconPacket.ReadAsync(stream, CancellationToken.None));
    }
}
