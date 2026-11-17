using WaNut.Bridge.Discord;
using WaNut.Bridge.GameLog;
using WaNut.Bridge.Relay;
using WaNut.Core.Bridge;

namespace WaNut.Bridge.Tests.Relay;

public sealed class MessageFormatterTests
{
    private static readonly GameEvent Chat = new(GameEventKind.Chat, "Steve", "hi *all*");

    [Fact]
    public void Format_ChatAsWebhook_PostsAsThePlayerWithTheirHead()
    {
        Assert.Equal(
            new OutgoingMessage(@"hi \*all\*", "Steve", "https://mc-heads.net/avatar/Steve/64"),
            MessageFormatter.Format(Chat, DiscordPresentation.Webhook, BridgeTexts.English));
    }

    [Fact]
    public void Format_ChatAsWebhookWithNameDiscordRefuses_NamesThePlayerInTheText()
    {
        var message = MessageFormatter.Format(Chat with { Player = "discord_fan" }, DiscordPresentation.Webhook, BridgeTexts.English);

        Assert.Null(message.Username);
        Assert.Equal(@"**discord\_fan**: hi \*all\*", message.Text);
    }

    [Fact]
    public void Format_ChatAsEmbed_IsAGrayCard()
    {
        Assert.Equal(new OutgoingMessage(@"**Steve**: hi \*all\*", EmbedColor: MessageFormatter.ChatColor), MessageFormatter.Format(Chat, DiscordPresentation.Embed, BridgeTexts.English));
    }

    [Fact]
    public void Format_ChatAsPlainText_UsesTheMcPrefix()
    {
        Assert.Equal(@"[MC] <Steve> hi \*all\*", MessageFormatter.Format(Chat, DiscordPresentation.PlainText, BridgeTexts.English).Text);
    }

    [Theory]
    [InlineData(GameEventKind.Joined, MessageFormatter.JoinColor)]
    [InlineData(GameEventKind.Left, MessageFormatter.LeaveColor)]
    public void Format_JoinLeaveAsEmbed_UsesGreenAndRed(GameEventKind kind, int color)
    {
        Assert.Equal(color, MessageFormatter.Format(new GameEvent(kind, "Steve"), DiscordPresentation.Embed, BridgeTexts.English).EmbedColor);
    }

    [Fact]
    public void Format_DeathAsWebhook_IsBoldServerLine()
    {
        var message = MessageFormatter.Format(new GameEvent(GameEventKind.Death, "Steve", "Steve was slain by Zombie"), DiscordPresentation.Webhook, BridgeTexts.English);

        Assert.Equal(new OutgoingMessage("**Steve was slain by Zombie**"), message);
    }

    [Fact]
    public void Format_JoinInChinese_UsesChineseText()
    {
        var message = MessageFormatter.Format(new GameEvent(GameEventKind.Joined, "Steve"), DiscordPresentation.PlainText, BridgeTexts.For("zh-TW"));

        Assert.Equal("[MC] Steve 加入了遊戲", message.Text);
    }
}
