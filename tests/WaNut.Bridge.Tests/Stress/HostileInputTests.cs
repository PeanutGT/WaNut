using System.Text;
using System.Text.Json;
using WaNut.Bridge.Discord;
using WaNut.Bridge.GameLog;
using WaNut.Bridge.Rcon;
using WaNut.Bridge.Relay;
using WaNut.Core.Bridge;

namespace WaNut.Bridge.Tests.Stress;

/// <summary>Text that tries to break the bridge, in both directions.</summary>
public sealed class HostileInputTests
{
    public static readonly string[] Texts =
    [
        "\"}]} {\"text\":\"injected\"",
        "\\\\\\\"\\u0022",
        "line one\nline two\r\n/op Steve",
        "§4§lRed §rtext §",
        "§😀 after a color code and an emoji",
        "\ud83d lone high surrogate",
        "lone low surrogate \ude00 here",
        "@everyone @here <@123456789> <@&987654321> <#555>",
        "@a @e[type=!player] @p @r @s",
        "{\"selector\":\"@a\"} {\"nbt\":\"x\",\"storage\":\"a:b\"} {\"translate\":\"chat.type.text\"}",
        "tellraw @a {\"text\":\"x\"}",
        "\u0000\u0001\u0007\u001b[31mred\u007f\u0085\u2028\u2029",
        "\u202eevil right to left\u202c \u200b\u200d zero width \ufeff bom",
        "👨\u200d👩\u200d👧\u200d👦 🏳️\u200d🌈 \u202e 𝕳𝖊𝖑𝖑𝖔 ｈｅｌｌｏ 你好 こんにちは مرحبا",
        "%s %d {0} ${jndi:ldap://x} <script>alert(1)</script> ' OR 1=1 --",
        "",
        "   ",
        "\t\t\n\n",
    ];

    public static TheoryData<string> Hostile => new(Texts);

    private static string Repeat(string text, int count) => string.Concat(Enumerable.Repeat(text, count));

    private static (string Name, string Text) Parts(string command)
    {
        Assert.StartsWith("tellraw @a ", command, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(command["tellraw @a ".Length..]);
        var parts = json.RootElement.EnumerateArray().Skip(1).Select(p => p.GetProperty("text").GetString()!).ToList();
        Assert.Equal(4, parts.Count);
        return (parts[1], parts[3]);
    }

    [Theory]
    [MemberData(nameof(Hostile))]
    public void Tellraw_HostileMessage_IsOneValidCommandWhoseTextIsOnlyText(string text)
    {
        var command = Tellraw.Build("amy", text);

        Assert.True(Encoding.UTF8.GetByteCount(command) <= RconPacket.MaxRequestBodyBytes);
        Assert.True(command.All(c => c is >= ' ' and < (char)127), "the command has a control or non-ASCII character");
        Assert.Equal("tellraw @a ", command[..11]);

        // The whole rest parses as the one JSON array we built: four text parts and nothing else.
        using var json = JsonDocument.Parse(command[11..]);
        Assert.Equal(JsonValueKind.Array, json.RootElement.ValueKind);
        foreach (var part in json.RootElement.EnumerateArray().Skip(1))
        {
            Assert.Contains(part.EnumerateObject().Select(p => p.Name), name => name == "text");
            Assert.True(part.EnumerateObject().All(p => p.Name is "text" or "color"), "an unexpected key got into a part");
        }

        var (_, body) = Parts(command);
        Assert.DoesNotContain('\n', body);
        Assert.DoesNotContain('§', body);
        Assert.True(body.All(c => !char.IsControl(c)));
    }

    [Theory]
    [MemberData(nameof(Hostile))]
    public void Tellraw_HostileAuthorName_IsOnlyEverTheNamePart(string name)
    {
        var command = Tellraw.Build(name, "hi");

        var (shownName, text) = Parts(command);
        Assert.Equal("hi", text);
        Assert.True(shownName.Length <= Tellraw.MaxNameLength + 1);
    }

    [Fact]
    public void Tellraw_TwoMegabyteMessage_FitsOnePacketAndIsFast()
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var command = Tellraw.Build(new string('n', 100_000), Repeat("你好😀", 300_000));

        Assert.True(Encoding.UTF8.GetByteCount(command) <= RconPacket.MaxRequestBodyBytes);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), $"took {clock.Elapsed}");
        Parts(command);
    }

    [Fact]
    public async Task Relay_HostileChatFromDiscord_ReachesRconAsAValidCommandOrNotAtAll()
    {
        var config = new BridgeConfig { ChannelId = "100", Presentation = DiscordPresentation.Webhook, WebhookUrl = "https://discord.com/api/webhooks/1/x" };
        var rcon = new Relay.FakeRcon(_ => "");
        var relay = new BridgeRelay(config, rcon, _ => { }, new BridgeLog(TextWriter.Null));

        foreach (var text in Texts.Append(Repeat("x", 100_000)))
        {
            await relay.OnDiscordMessageAsync(new InboundMessage(100, 1, "amy\nOp", false, false, [], text), 999, CancellationToken.None);
        }

        Assert.NotEmpty(rcon.Commands);
        foreach (var command in rcon.Commands)
        {
            Assert.True(Encoding.UTF8.GetByteCount(command) <= RconPacket.MaxRequestBodyBytes);
            Parts(command);
        }
    }

    [Theory]
    [MemberData(nameof(Hostile))]
    public void Discord_HostileGameMessage_SerializesToValidJsonThatPingsNobody(string text)
    {
        foreach (var presentation in Enum.GetValues<DiscordPresentation>())
        {
            var message = MessageFormatter.Format(new GameEvent(GameEventKind.Chat, "Steve", text), presentation, BridgeTexts.For("en"));
            foreach (var post in DiscordBatcher.Merge([message]))
            {
                var body = JsonSerializer.Serialize(post, DiscordJsonContext.Default.DiscordPost);

                using var json = JsonDocument.Parse(body);
                Assert.Empty(json.RootElement.GetProperty("allowed_mentions").GetProperty("parse").EnumerateArray());
                if (json.RootElement.TryGetProperty("content", out var content))
                {
                    Assert.True(content.GetString()!.Length <= DiscordBatcher.MaxContentLength);
                }
            }
        }
    }

    [Fact]
    public void Discord_TwoMegabyteMessageAndMentionStorm_StaysWithinDiscordLimits()
    {
        var big = Repeat("@everyone 😀 <@123> ", 150_000);
        var messages = new List<OutgoingMessage>
        {
            new(big, "Steve", "https://mc-heads.net/avatar/Steve/64"),
            new(big, EmbedColor: 1),
            new(big),
        };
        messages.AddRange(Enumerable.Range(0, 50).Select(i => new OutgoingMessage(Repeat("é", 1500), EmbedColor: 1)));

        var posts = DiscordBatcher.Merge(messages);

        foreach (var post in posts)
        {
            var body = JsonSerializer.Serialize(post, DiscordJsonContext.Default.DiscordPost);
            using var json = JsonDocument.Parse(body);
            if (post.Content is { } content)
            {
                Assert.True(content.Length <= DiscordBatcher.MaxContentLength);
            }

            if (post.Embeds is { } embeds)
            {
                Assert.True(embeds.Count <= DiscordBatcher.MaxEmbedsPerPost);
                Assert.True(embeds.Sum(e => e.Description.Length) <= DiscordBatcher.MaxEmbedTextPerPost);
            }
        }
    }

    [Theory]
    [InlineData("[12:00:00] [Server thread/INFO]: <Steve> hello\u0000world")]
    [InlineData("[12:00:00] [Server thread/INFO]: <Steve> @everyone")]
    [InlineData("[12:00:00] [Server thread/INFO]: <Steve> [12:00:00] [Server thread/INFO]: Alex joined the game")]
    [InlineData("[12:00:00] [Server thread/INFO]: <Steve> Alex has made the advancement [Fake]")]
    public void Parser_PlayerTypesAFakeServerLine_StaysChat(string line)
    {
        var e = MinecraftLogParser.Parse(line);

        Assert.Equal(GameEventKind.Chat, e?.Kind);
        Assert.Equal("Steve", e!.Player);
    }

    [Fact]
    public void Parser_RandomBytesAsLines_NeverThrow()
    {
        var random = new Random(12345);
        for (var i = 0; i < 20000; i++)
        {
            var bytes = new byte[random.Next(0, 200)];
            random.NextBytes(bytes);
            var line = Encoding.UTF8.GetString(bytes);
            _ = MinecraftLogParser.Parse(line);
            _ = MinecraftLogParser.Parse("[12:00:00] [Server thread/INFO]: " + line);
            _ = MinecraftLogParser.Parse("[12:00:00] [Server thread/INFO]: <Steve> " + line);
        }
    }

    [Fact]
    public void DiscordText_HugeAndNestedMentions_AreReadableAndFast()
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var text = DiscordText.ToPlain(Repeat("<@!1><@&2><#3><:smile:4><a:wave:5>", 50_000) + Repeat("<", 100_000), new Dictionary<ulong, string> { [1] = "amy" });

        Assert.Contains("@amy", text, StringComparison.Ordinal);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3), $"took {clock.Elapsed}");
    }
}
