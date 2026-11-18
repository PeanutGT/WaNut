using System.Text.Json;
using WaNut.Bridge.Discord;
using WaNut.Bridge.Discord.Gateway;

namespace WaNut.Bridge.Tests.Discord.Gateway;

public sealed class GatewayEventsTests
{
    [Fact]
    public void Message_GuildMessage_ReadsNicknameRolesAndMentions()
    {
        var message = GatewayEvents.Message(Parse("""
            {"type":0,"channel_id":"100","content":"hi <@42>","author":{"id":"1","username":"amy","global_name":"Amy G"},
             "member":{"nick":"Captain","roles":["5","6"]},"mentions":[{"id":"42","username":"bob","member":{"nick":"Bobby"}}],
             "attachments":[{"id":"9","url":"https://cdn.discordapp.com/attachments/1/2/a.png?ex=1","filename":"a.png","content_type":"image/png","size":2048}]}
            """));

        var roles = message!.RoleIds;
        Assert.Equal("hi @Bobby", message.Content);
        Assert.Equal(new InboundAttachment("https://cdn.discordapp.com/attachments/1/2/a.png?ex=1", "a.png", "image/png", 2048), Assert.Single(message.Files));
        Assert.Equal([5UL, 6UL], roles);
    }

    [Fact]
    public void Message_Reply_ReadsWhoAndWhatItAnswers()
    {
        var message = GatewayEvents.Message(Parse("""
            {"type":19,"channel_id":"100","content":"agreed","author":{"id":"1","username":"amy"},
             "referenced_message":{"content":"shall we build <@42>?","author":{"id":"2","username":"bob","global_name":"Bob G"}},
             "mentions":[{"id":"42","username":"cat"}]}
            """));

        Assert.Equal(new InboundReply("Bob G", "shall we build @cat?"), message!.Reply);
    }

    [Fact]
    public void Message_ReplyToDeletedMessageOrPlainMessage_HasNoReply()
    {
        var deleted = GatewayEvents.Message(Parse("""{"type":19,"channel_id":"1","content":"x","author":{"id":"1","username":"a"},"referenced_message":null}"""));
        var plain = GatewayEvents.Message(Parse("""{"type":0,"channel_id":"1","content":"x","author":{"id":"1","username":"a"}}"""));

        Assert.Null(deleted!.Reply);
        Assert.Null(plain!.Reply);
    }

    [Fact]
    public void Message_FromWebhookOrBot_IsMarked()
    {
        var message = GatewayEvents.Message(Parse("""{"type":0,"channel_id":"1","content":"x","webhook_id":"3","author":{"id":"2","username":"hook","bot":true}}"""));

        Assert.True(message!.IsBot);
        Assert.True(message.IsWebhook);
    }

    [Fact]
    public void Message_SystemMessage_IsNull()
    {
        Assert.Null(GatewayEvents.Message(Parse("""{"type":7,"channel_id":"1","content":"","author":{"id":"2","username":"x"}}""")));
    }

    [Fact]
    public void Command_SlashCommand_ReadsOptionsAndUser()
    {
        var command = GatewayEvents.Command(Parse("""
            {"type":2,"id":"11","token":"tok","application_id":"777","channel_id":"100",
             "member":{"user":{"id":"1","username":"amy"},"roles":["5"]},
             "data":{"name":"cmd","options":[{"name":"command","type":3,"value":"time set day"}]}}
            """));

        Assert.Equal("cmd", command!.Name);
        Assert.Equal("time set day", command.Options["command"]);
        Assert.Equal(("11", "tok", "777", 100UL, "amy"), (command.InteractionId, command.Token, command.ApplicationId, command.ChannelId, command.UserName));
        Assert.Equal([5UL], command.RoleIds);
    }

    [Fact]
    public void Command_InDirectMessage_IsNull()
    {
        Assert.Null(GatewayEvents.Command(Parse("""{"type":2,"id":"1","token":"t","user":{"id":"1"},"data":{"name":"list"}}""")));
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
