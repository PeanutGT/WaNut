using WaNut.Core.Hosting;

namespace WaNut.Core.Tests.Hosting;

public sealed class ServerLogParserTests
{
    [Theory]
    [InlineData("[12:34:56] [Server thread/INFO]: Done (3.456s)! For help, type \"help\"")]
    [InlineData("[12:34:56 INFO]: Done (12.003s)! For help, type \"help\"")]
    [InlineData("[12:34:56] [Server thread/INFO]: Done (3,456s)! For help, type \"help\"")]
    [InlineData("[12:34:56] [Server thread/INFO] [minecraft/DedicatedServer]: Done (9.120s)! For help, type \"help\"")]
    [InlineData("[16Oct2026 12:34:56.789] [Server thread/INFO] [net.minecraft.server.dedicated.DedicatedServer/]: Done (9.120s)! For help, type \"help\"")]
    public void Parse_DoneLine_ReturnsOnline(string line)
    {
        Assert.Equal(new LogEvent(LogEventKind.Online), ServerLogParser.Parse(line));
    }

    [Theory]
    [InlineData("[12:35:00] [Server thread/INFO]: Steve joined the game", LogEventKind.PlayerJoined, "Steve")]
    [InlineData("[12:35:00 INFO]: Steve joined the game", LogEventKind.PlayerJoined, "Steve")]
    [InlineData("[12:35:00 INFO]: Steve joined the game.", LogEventKind.PlayerJoined, "Steve")]
    [InlineData("[12:35:00 INFO]: §eSteve joined the game", LogEventKind.PlayerJoined, "Steve")]
    [InlineData("[12:35:00 INFO]: .Steve joined the game", LogEventKind.PlayerJoined, ".Steve")]
    [InlineData("[12:35:00 INFO]: Steve 加入了遊戲", LogEventKind.PlayerJoined, "Steve")]
    [InlineData("[12:35:00 INFO]: Steve[/127.0.0.1:54321] logged in with entity id 42 at ([world]0.0, 64.0, 0.0)", LogEventKind.PlayerJoined, "Steve")]
    [InlineData("[12:40:00] [Server thread/INFO]: Steve left the game", LogEventKind.PlayerLeft, "Steve")]
    [InlineData("[12:40:00 INFO]: Steve left the game.", LogEventKind.PlayerLeft, "Steve")]
    [InlineData("[12:40:00 INFO]: §eSteve left the game.", LogEventKind.PlayerLeft, "Steve")]
    [InlineData("[12:40:00 INFO]: *Steve left the game", LogEventKind.PlayerLeft, "*Steve")]
    [InlineData("[12:40:00 INFO]: Steve 離開了伺服器", LogEventKind.PlayerLeft, "Steve")]
    [InlineData("[12:40:00 INFO]: Steve lost connection: Disconnected", LogEventKind.PlayerLeft, "Steve")]
    [InlineData("[12:40:00 INFO]: Steve lost connection: Timed out", LogEventKind.PlayerLeft, "Steve")]
    [InlineData("[12:35:00] [Server thread/INFO] [minecraft/MinecraftServer]: Steve joined the game", LogEventKind.PlayerJoined, "Steve")]
    [InlineData("[16Oct2026 12:40:00.123] [Server thread/INFO] [net.minecraft.server.MinecraftServer/]: Steve left the game", LogEventKind.PlayerLeft, "Steve")]
    public void Parse_PlayerLine_ReturnsPlayerEvent(string line, LogEventKind expected, string expectedName)
    {
        Assert.Equal(new LogEvent(expected, expectedName), ServerLogParser.Parse(line));
    }

    [Theory]
    [InlineData("[12:50:00] [Server thread/INFO]: Saved the game")]
    [InlineData("[12:50:00 INFO]: Saved the game")]
    public void Parse_SavedLine_ReturnsSaved(string line)
    {
        Assert.Equal(new LogEvent(LogEventKind.Saved), ServerLogParser.Parse(line));
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

    [Fact]
    public void Clean_SectionColorCodes_AreRemoved()
    {
        Assert.Equal("[12:00:00 INFO]: Steve joined the game", ServerLogParser.Clean("[12:00:00 INFO]: §eSteve §rjoined the game"));
    }

    [Theory]
    [InlineData("[12:34:56] [Server thread/INFO]: Starting minecraft server version 1.21.1", LogLineLevel.Info)]
    [InlineData("[12:34:56] [Server thread/WARN]: Can't keep up! Is the server overloaded?", LogLineLevel.Warning)]
    [InlineData("[12:34:56 WARN]: Can't keep up!", LogLineLevel.Warning)]
    [InlineData("[12:34:56] [Server thread/ERROR]: Encountered an unexpected exception", LogLineLevel.Error)]
    [InlineData("[12:34:56 ERROR]: Could not pass event", LogLineLevel.Error)]
    [InlineData("[12:34:56] [main/FATAL]: Failed to start the minecraft server", LogLineLevel.Error)]
    [InlineData("\tat net.minecraft.server.Main.main(Main.java:1)", LogLineLevel.Info)]
    [InlineData("[12:35:00] [Server thread/INFO]: <Alex> [x/ERROR]: hi", LogLineLevel.Info)]
    public void GetLevel_Line_ReturnsLevelFromPrefix(string line, LogLineLevel expected)
    {
        Assert.Equal(expected, ServerLogParser.GetLevel(line));
    }
}
