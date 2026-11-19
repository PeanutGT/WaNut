using WaNut.Bridge.GameLog;

namespace WaNut.Bridge.Tests.GameLog;

public sealed class MinecraftLogParserTests
{
    [Theory]
    [InlineData("[12:00:00] [Server thread/INFO]: <Steve> hello there")]
    [InlineData("[12:00:00 INFO]: <Steve> hello there")]
    [InlineData("[12:00:00] [Async Chat Thread - #0/INFO]: <Steve> hello there")]
    [InlineData("[12:00:00] [Server thread/INFO] [minecraft/MinecraftServer]: <Steve> hello there")]
    [InlineData("[12:00:00] [Server thread/INFO] [net.minecraft.server.MinecraftServer/]: <Steve> hello there")]
    [InlineData("[12:00:00] [Server thread/INFO]: [Not Secure] <Steve> hello there")]
    [InlineData("\u001b[32m[12:00:00 INFO]: <Steve> hello there\u001b[0m")]
    public void Parse_ChatOnEveryServerType_ReturnsChat(string line)
    {
        Assert.Equal(new GameEvent(GameEventKind.Chat, "Steve", "hello there"), MinecraftLogParser.Parse(line));
    }

    [Theory]
    [InlineData("[19:54:25 INFO]: [Not Secure] Steve: hello there")]
    [InlineData("[19:54:25 INFO]: System chat: <Steve> hello there")]
    public void Parse_Paper26Chat_ReturnsChat(string line)
    {
        Assert.Equal(new GameEvent(GameEventKind.Chat, "Steve", "hello there"), MinecraftLogParser.Parse(line));
    }

    [Fact]
    public void Parse_Paper26SignedChat_IsChatOnlyForAnOnlinePlayer()
    {
        const string line = "[19:54:25 INFO]: Steve: hello there";

        Assert.Equal(new GameEvent(GameEventKind.Chat, "Steve", "hello there"), MinecraftLogParser.Parse(line, name => name == "Steve"));
        Assert.Null(MinecraftLogParser.Parse(line, _ => false));
        Assert.Null(MinecraftLogParser.Parse(line));
    }

    [Theory]
    [InlineData("[19:54:25 INFO]: [Not Secure] [VIP] Steve: hello there")]
    [InlineData("[19:54:25 INFO]: [Admin] Steve: hello there")]
    [InlineData("[19:54:25 INFO]: Admin | Steve » hello there")]
    [InlineData("[19:54:25 INFO]: [Not Secure] Steve » hello there")]
    public void Parse_ChatPluginPrefix_IsChatForAnOnlinePlayer(string line)
    {
        Assert.Equal(new GameEvent(GameEventKind.Chat, "Steve", "hello there"), MinecraftLogParser.Parse(line, name => name == "Steve"));
        Assert.Null(MinecraftLogParser.Parse(line, _ => false));
    }

    [Fact]
    public void Parse_CommandFeedbackWithOnlineName_IsNotChat()
    {
        Assert.Null(MinecraftLogParser.Parse("[12:00:00] [Server thread/INFO]: System chat: [Steve: Teleported Steve to 0.5, 0.0, 0.5]", name => name == "Steve"));
    }

    [Fact]
    public void Parse_PluginLineThatLooksLikePaper26Chat_IsNotChat()
    {
        Assert.Null(MinecraftLogParser.Parse("[19:54:25 INFO]: Timings: reset", name => name == "Steve"));
    }

    [Theory]
    [InlineData("[12:00:00] [Server thread/INFO]: System chat: Steve joined the game", GameEventKind.Joined)]
    [InlineData("[12:00:00] [Server thread/INFO]: System chat: Steve left the game", GameEventKind.Left)]
    public void Parse_Minecraft26SystemChatPrefix_IsDropped(string line, GameEventKind kind)
    {
        Assert.Equal(new GameEvent(kind, "Steve"), MinecraftLogParser.Parse(line));
    }

    [Fact]
    public void Parse_Minecraft26DeathWithSystemChatPrefix_ReturnsDeath()
    {
        Assert.Equal(
            new GameEvent(GameEventKind.Death, "Steve", "Steve fell from a high place"),
            MinecraftLogParser.Parse("[12:00:00] [Server thread/INFO]: System chat: Steve fell from a high place"));
    }

    [Fact]
    public void Parse_ChatThatLooksLikeAJoin_StaysChat()
    {
        var e = MinecraftLogParser.Parse("[12:00:00] [Server thread/INFO]: <Steve> Alex joined the game");

        Assert.Equal(GameEventKind.Chat, e?.Kind);
        Assert.Equal("Alex joined the game", e?.Text);
    }

    [Theory]
    [InlineData("[12:00:00] [Server thread/INFO]: Steve joined the game", GameEventKind.Joined)]
    [InlineData("[12:00:00 INFO]: Steve left the game", GameEventKind.Left)]
    public void Parse_JoinAndLeave_ReturnsPlayer(string line, GameEventKind kind)
    {
        Assert.Equal(new GameEvent(kind, "Steve"), MinecraftLogParser.Parse(line));
    }

    [Theory]
    [InlineData("[12:00:00] [Server thread/INFO]: Done (3.512s)! For help, type \"help\"", GameEventKind.ServerStarted)]
    [InlineData("[12:00:00 INFO]: Done (12,1s)! For help, type \"help\" or \"?\"", GameEventKind.ServerStarted)]
    [InlineData("[12:00:00] [Server thread/INFO]: Stopping server", GameEventKind.ServerStopping)]
    public void Parse_ServerStartAndStop_ReturnsState(string line, GameEventKind kind)
    {
        Assert.Equal(kind, MinecraftLogParser.Parse(line)?.Kind);
    }

    [Theory]
    [InlineData("Steve was slain by Zombie")]
    [InlineData("Steve was shot by Skeleton using [Bow]")]
    [InlineData("Steve drowned")]
    [InlineData("Steve fell from a high place")]
    [InlineData("Steve hit the ground too hard")]
    [InlineData("Steve tried to swim in lava")]
    [InlineData("Steve blew up")]
    [InlineData("Steve died")]
    [InlineData("Steve starved to death")]
    [InlineData("Steve walked into fire while fighting Zombie")]
    [InlineData("Steve left the confines of this world")]
    [InlineData("Steve experienced kinetic energy")]
    [InlineData("Steve didn't want to live in the same world as Alex")]
    public void Parse_DeathMessages_ReturnsDeathWithWholeMessage(string message)
    {
        var e = MinecraftLogParser.Parse("[12:00:00] [Server thread/INFO]: " + message);

        Assert.Equal(new GameEvent(GameEventKind.Death, "Steve", message), e);
    }

    [Theory]
    [InlineData("Steve has made the advancement [Stone Age]")]
    [InlineData("Steve has completed the challenge [Monsters Hunted]")]
    [InlineData("Steve has reached the goal [Sky's the Limit]")]
    public void Parse_Advancements_ReturnsAdvancement(string message)
    {
        Assert.Equal(new GameEvent(GameEventKind.Advancement, "Steve", message), MinecraftLogParser.Parse("[12:00:00 INFO]: " + message));
    }

    [Theory]
    [InlineData("[12:00:00 INFO]: Steve issued server command: /msg Alex secret plans")]
    [InlineData("[12:00:00 INFO]: Steve issued server command: /tell Alex secret plans")]
    [InlineData("[12:00:00] [Server thread/INFO]: [Steve: Whispered to Alex: secret plans]")]
    [InlineData("[12:00:00] [Server thread/INFO]: Named entity EntityWolf['Rex'/42] died: Rex was slain by Zombie")]
    [InlineData("[12:00:00] [Server thread/INFO]: Villager EntityVillager['Villager'/7] died, message: 'Villager was slain by Zombie'")]
    [InlineData("[12:00:00] [Server thread/INFO]: Preparing level \"world\"")]
    [InlineData("[12:00:00] [Server thread/WARN]: Can't keep up! Is the server overloaded?")]
    [InlineData("no prefix <Steve> hello")]
    public void Parse_OtherLines_ReturnsNull(string line)
    {
        Assert.Null(MinecraftLogParser.Parse(line));
    }
}
