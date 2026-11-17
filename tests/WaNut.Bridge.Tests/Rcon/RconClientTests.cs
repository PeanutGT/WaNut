using WaNut.Bridge.Rcon;

namespace WaNut.Bridge.Tests.Rcon;

public sealed class RconClientTests
{
    [Fact]
    public async Task ExecuteAsync_SignedIn_ReturnsTheServersAnswer()
    {
        await using var server = new FakeRconServer("secret", command => command == "list" ? "There are 0 of a max of 20 players online: " : string.Empty);
        await using var client = await RconClient.ConnectAsync("127.0.0.1", server.Port, "secret", CancellationToken.None);

        var answer = await client.ExecuteAsync("list", CancellationToken.None);

        Assert.Equal("There are 0 of a max of 20 players online: ", answer);
        Assert.Equal(["list"], server.Commands);
    }

    [Fact]
    public async Task ExecuteAsync_AnswerForAnotherRequestFirst_SkipsIt()
    {
        await using var server = new FakeRconServer("secret", _ => "mine", sendStrayPacketFirst: true);
        await using var client = await RconClient.ConnectAsync("127.0.0.1", server.Port, "secret", CancellationToken.None);

        Assert.Equal("mine", await client.ExecuteAsync("say hi", CancellationToken.None));
    }

    [Fact]
    public async Task ConnectAsync_WrongPassword_Throws()
    {
        await using var server = new FakeRconServer("secret", _ => string.Empty);

        await Assert.ThrowsAsync<RconAuthenticationException>(() => RconClient.ConnectAsync("127.0.0.1", server.Port, "wrong", CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_CommandTooLong_ThrowsBeforeSending()
    {
        await using var server = new FakeRconServer("secret", _ => string.Empty);
        await using var client = await RconClient.ConnectAsync("127.0.0.1", server.Port, "secret", CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentException>(() => client.ExecuteAsync(new string('a', RconPacket.MaxRequestBodyBytes + 1), CancellationToken.None));
        Assert.Empty(server.Commands);
    }
}
