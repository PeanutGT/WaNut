using WaNut.Core.Hosting;

namespace WaNut.Core.Tests.Hosting;

public sealed class ServerLogParserTests
{
    [Theory]
    [InlineData("[12:34:56] [Server thread/INFO]: Done (3.456s)! For help, type \"help\"")]
    [InlineData("[12:34:56 INFO]: Done (12.003s)! For help, type \"help\"")]
    [InlineData("[12:34:56] [Server thread/INFO]: Done (3,456s)! For help, type \"help\"")]
    public void Parse_DoneLine_ReturnsOnline(string line)
    {
        Assert.Equal(new LogEvent(LogEventKind.Online), ServerLogParser.Parse(line));
    }

    [Theory]
    [InlineData("[12:35:00] [Server thread/INFO]: Steve joined the game", LogEventKind.PlayerJoined)]
    [InlineData("[12:35:00 INFO]: Steve joined the game", LogEventKind.PlayerJoined)]
    [InlineData("[12:40:00] [Server thread/INFO]: Steve left the game", LogEventKind.PlayerLeft)]
    public void Parse_PlayerLine_ReturnsPlayerEvent(string line, LogEventKind expected)
    {
        Assert.Equal(new LogEvent(expected, "Steve"), ServerLogParser.Parse(line));
    }

    [Fact]
    public void Parse_StoppingLine_ReturnsStopping()
    {
        Assert.Equal(new LogEvent(LogEventKind.Stopping), ServerLogParser.Parse("[12:50:00] [Server thread/INFO]: Stopping server"));
    }

    [Theory]
    [InlineData("[12:35:00] [Server thread/INFO]: <Alex> Steve joined the game")]
    [InlineData("[12:35:00] [Server thread/INFO]: <Alex> x]: Steve joined the game")]
    [InlineData("[12:35:00] [Server thread/INFO]: [Not Secure] <Alex> Done (1.0s)! For help, type \"help\"")]
    public void Parse_ChatThatLooksLikeServerMessage_ReturnsNull(string line)
    {
        Assert.Null(ServerLogParser.Parse(line));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Starting net.minecraft.server.Main")]
    [InlineData("[12:00:00] [Server thread/INFO]: Preparing level \"world\"")]
    public void Parse_OrdinaryLine_ReturnsNull(string line)
    {
        Assert.Null(ServerLogParser.Parse(line));
    }

    [Fact]
    public void Clean_AnsiColors_AreRemoved()
    {
        Assert.Equal("[12:00:00 INFO]: Hello", ServerLogParser.Clean("\u001b[32m[12:00:00 INFO]: Hello\u001b[0m"));
    }
}
