using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using WaNut.Core.Presence;

namespace WaNut.Core.Tests.Presence;

public sealed class DiscordIpcClientTests
{
    private static readonly PresenceActivity Activity = new("My server", "Paper 1.21.4 · 3 online", DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));

    [Fact]
    public void BuildCommand_Activity_HasLinesStartTimeAndOnlyTheProjectButton()
    {
        using var json = JsonDocument.Parse(PresenceActivity.BuildCommand(Activity, 42, "n1"));

        var args = json.RootElement.GetProperty("args");
        var activity = args.GetProperty("activity");
        Assert.Equal("SET_ACTIVITY", json.RootElement.GetProperty("cmd").GetString());
        Assert.Equal(42, args.GetProperty("pid").GetInt32());
        Assert.Equal("My server", activity.GetProperty("details").GetString());
        Assert.Equal(1_700_000_000, activity.GetProperty("timestamps").GetProperty("start").GetInt64());
        var buttons = activity.GetProperty("buttons");
        Assert.Equal(1, buttons.GetArrayLength());
        Assert.Equal(PresenceActivity.ButtonUrl, buttons[0].GetProperty("url").GetString());
    }

    [Fact]
    public void BuildCommand_Null_ClearsTheActivity()
    {
        using var json = JsonDocument.Parse(PresenceActivity.BuildCommand(null, 42, "n1"));

        Assert.False(json.RootElement.GetProperty("args").TryGetProperty("activity", out _));
    }

    [Fact]
    public void BuildCommand_ShortOrLongLines_FitDiscordsLimits()
    {
        var odd = new PresenceActivity("A", new string('x', 300), DateTimeOffset.UnixEpoch);

        using var json = JsonDocument.Parse(PresenceActivity.BuildCommand(odd, 1, "n"));

        var activity = json.RootElement.GetProperty("args").GetProperty("activity");
        Assert.Equal(2, activity.GetProperty("details").GetString()!.Length);
        Assert.Equal(PresenceActivity.MaxLength, activity.GetProperty("state").GetString()!.Length);
    }

    [Fact]
    public async Task SetActivityAsync_HandshakesOnceThenSendsFrames()
    {
        var pipe = new ScriptedStream(Reply("""{"evt":"READY"}"""), Reply("""{"evt":null}"""), Reply("""{"evt":null}"""));
        using var client = new DiscordIpcClient("123", (i, _) => Task.FromResult<Stream?>(i == 0 ? pipe : null));

        Assert.True(await client.SetActivityAsync(Activity, CancellationToken.None));
        Assert.True(await client.SetActivityAsync(null, CancellationToken.None));

        var frames = pipe.WrittenFrames();
        Assert.Equal(3, frames.Count);
        Assert.Equal((0, """{"v":1,"client_id":"123"}"""), frames[0]);
        Assert.Equal(1, frames[1].Opcode);
        Assert.Contains("SET_ACTIVITY", frames[2].Json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetActivityAsync_DiscordNotRunning_IsFalseNotAnError()
    {
        using var client = new DiscordIpcClient("123", (_, _) => Task.FromResult<Stream?>(null));

        Assert.False(await client.SetActivityAsync(Activity, CancellationToken.None));
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task SetActivityAsync_PipeDropsMidway_ForgetsTheConnectionSoItCanReconnect()
    {
        var first = new ScriptedStream(Reply("""{"evt":"READY"}"""));
        var second = new ScriptedStream(Reply("""{"evt":"READY"}"""), Reply("""{"evt":null}"""));
        var streams = new Queue<Stream>([first, second]);
        using var client = new DiscordIpcClient("123", (i, _) => Task.FromResult<Stream?>(i == 0 && streams.Count > 0 ? streams.Dequeue() : null));

        Assert.False(await client.SetActivityAsync(Activity, CancellationToken.None));
        Assert.True(await client.SetActivityAsync(Activity, CancellationToken.None));
    }

    private static byte[] Reply(string json)
    {
        var body = Encoding.UTF8.GetBytes(json);
        var frame = new byte[8 + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, 1);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), body.Length);
        body.CopyTo(frame, 8);
        return frame;
    }

    private sealed class ScriptedStream : Stream
    {
        private readonly MemoryStream _incoming;
        private readonly MemoryStream _written = new();

        public ScriptedStream(params byte[][] replies) => _incoming = new MemoryStream([.. replies.SelectMany(r => r)]);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public List<(int Opcode, string Json)> WrittenFrames()
        {
            var bytes = _written.ToArray();
            var frames = new List<(int, string)>();
            for (var at = 0; at < bytes.Length;)
            {
                var length = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at + 4));
                frames.Add((BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at)), Encoding.UTF8.GetString(bytes, at + 8, length)));
                at += 8 + length;
            }

            return frames;
        }

        public override int Read(byte[] buffer, int offset, int count) => _incoming.Read(buffer, offset, count);

        public override void Write(byte[] buffer, int offset, int count) => _written.Write(buffer, offset, count);

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
