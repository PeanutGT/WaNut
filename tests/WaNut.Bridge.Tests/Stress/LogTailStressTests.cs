using System.Diagnostics;
using System.Text;
using WaNut.Bridge.GameLog;

namespace WaNut.Bridge.Tests.Stress;

/// <summary>latest.log the way a busy, restarting server treats it.</summary>
public sealed class LogTailStressTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("wanut-tail-stress-").FullName;

    private string LogPath => Path.Combine(_folder, "latest.log");

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static string Header(string stamp) => $"[{stamp}] [main/INFO]: Starting minecraft server version 1.21";

    [Fact]
    public async Task Writer_AppendsHundredThousandLinesWhileTheReaderPolls_NothingLostOrRepeated()
    {
        File.WriteAllText(LogPath, Header("10:00:00") + "\n");
        var reader = new LogTailReader(LogPath);
        await reader.SkipToEndAsync();
        const int total = 100_000;
        var writer = Task.Run(() =>
        {
            using var stream = new FileStream(LogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            for (var i = 0; i < total; i += 500)
            {
                var batch = new StringBuilder();
                for (var j = i; j < i + 500; j++)
                {
                    batch.Append("[10:00:01] [Server thread/INFO]: <Steve> line ").Append(j).Append('\n');
                }

                var bytes = Encoding.UTF8.GetBytes(batch.ToString());
                // Written in two pieces so that polls also land in the middle of a line.
                stream.Write(bytes, 0, bytes.Length / 2 + 3);
                stream.Flush();
                stream.Write(bytes, bytes.Length / 2 + 3, bytes.Length - bytes.Length / 2 - 3);
                stream.Flush();
            }
        });

        var lines = new List<string>();
        while (lines.Count < total)
        {
            lines.AddRange(await reader.PollAsync());
            if (writer.IsCompleted && lines.Count < total)
            {
                lines.AddRange(await reader.PollAsync());
                Assert.Equal(total, lines.Count);
            }
        }

        await writer;
        Assert.Empty(await reader.PollAsync());
        Assert.Equal(
            Enumerable.Range(0, total).Select(i => $"[10:00:01] [Server thread/INFO]: <Steve> line {i}"),
            lines);
    }

    [Fact]
    public async Task ServerRestartedTwentyTimes_EveryNewFileIsReadFromItsFirstLine()
    {
        File.WriteAllText(LogPath, Header("09:00:00") + "\n");
        var reader = new LogTailReader(LogPath);
        await reader.SkipToEndAsync();
        var seen = new List<string>();
        for (var run = 0; run < 20; run++)
        {
            var lines = Enumerable.Range(0, 50).Select(i => $"[10:{run:D2}:{i:D2}] [Server thread/INFO]: run {run} line {i}").ToList();
            File.AppendAllLines(LogPath, lines.Take(25));
            seen.AddRange(await reader.PollAsync());
            File.AppendAllLines(LogPath, lines.Skip(25));
            seen.AddRange(await reader.PollAsync());

            // Minecraft renames latest.log and creates a new one on the next start; here also a gap with no file at all.
            File.Move(LogPath, Path.Combine(_folder, $"old-{run}.log"));
            if (run % 3 == 0)
            {
                Assert.Empty(await reader.PollAsync());
            }

            File.WriteAllText(LogPath, Header($"11:{run:D2}:00") + "\n");
            seen.AddRange(await reader.PollAsync());
        }

        for (var run = 0; run < 20; run++)
        {
            Assert.Equal(
                Enumerable.Range(0, 50).Select(i => $"[10:{run:D2}:{i:D2}] [Server thread/INFO]: run {run} line {i}"),
                seen.Where(l => l.Contains($"run {run} line", StringComparison.Ordinal)));
        }

        Assert.Equal(20, seen.Count(l => l.Contains("Starting minecraft", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task FileEmptiedInPlaceAndRefilled_IsReadAgainFromTheTop()
    {
        // logrotate's copytruncate: same file, cut to zero, then a different head.
        File.WriteAllText(LogPath, Header("10:00:00") + "\nold 1\nold 2\n");
        var reader = new LogTailReader(LogPath);
        await reader.SkipToEndAsync();

        using (var stream = new FileStream(LogPath, FileMode.Truncate, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
        {
            Assert.Equal(0, stream.Length);
        }

        File.AppendAllText(LogPath, Header("10:05:00") + "\nnew 1\n");

        Assert.Equal([Header("10:05:00"), "new 1"], await reader.PollAsync());
    }

    [Fact]
    public async Task NewReaderAfterBridgeRestart_DoesNotRepeatOldLinesButSkipsWhatHappenedWhileDown()
    {
        File.WriteAllText(LogPath, Header("10:00:00") + "\nbefore\n");
        var first = new LogTailReader(LogPath);
        await first.SkipToEndAsync();
        File.AppendAllText(LogPath, "seen by first\n");
        Assert.Equal(["seen by first"], await first.PollAsync());

        // The bridge dies and comes back; the server went on writing.
        File.AppendAllText(LogPath, "written while the bridge was down\n");
        var second = new LogTailReader(LogPath);
        await second.SkipToEndAsync();
        File.AppendAllText(LogPath, "after restart\n");

        Assert.Equal(["after restart"], await second.PollAsync());
    }

    [Fact]
    public async Task HugeLineWithoutNewline_DoesNotGrowWithoutLimitAndLaterLinesStillCome()
    {
        File.WriteAllText(LogPath, Header("10:00:00") + "\n");
        var reader = new LogTailReader(LogPath);
        await reader.SkipToEndAsync();
        var before = GC.GetTotalMemory(forceFullCollection: true);

        // 40 MB with no line break, written in 1 MB pieces while the reader polls.
        var chunk = new string('x', 1024 * 1024);
        for (var i = 0; i < 40; i++)
        {
            File.AppendAllText(LogPath, chunk);
            Assert.Empty(await reader.PollAsync());
        }

        File.AppendAllText(LogPath, "\nnext line\n");
        var lines = await reader.PollAsync();
        var grown = GC.GetTotalMemory(forceFullCollection: true) - before;

        Assert.Equal("next line", lines[^1]);
        Assert.True(lines.Count <= 2);
        Assert.True(lines.All(l => l.Length <= LogTailReader.MaxLineLength + 1), "a line longer than the limit was delivered");
        Assert.True(grown < 8 * 1024 * 1024, $"the reader holds {grown / 1024 / 1024} MB after a huge line");
    }

    [Fact]
    public async Task ParserAndReader_OneMegabyteOfHostileLines_FinishQuickly()
    {
        var hostile = new StringBuilder();
        hostile.Append(Header("10:00:00")).Append('\n');
        for (var i = 0; i < 30; i++)
        {
            hostile.Append("[10:00:01] [Server thread/INFO]: <Steve> ").Append(new string('<', 20000)).Append('\n');
            hostile.Append("[10:00:01] [Server thread/INFO]: ").Append(new string('A', 20000)).Append(" was ").Append('\n');
            hostile.Append("[10:00:01] [Server thread/INFO]: ").Append(string.Concat(Enumerable.Repeat("]: ", 7000))).Append('\n');
            hostile.Append("[10:00:01] [Server thread/INFO]: Steve has made the advancement [").Append(new string('[', 20000)).Append('\n');
        }

        File.WriteAllText(LogPath, hostile.ToString());
        var reader = new LogTailReader(LogPath);
        var clock = Stopwatch.StartNew();
        var lines = await reader.PollAsync();
        foreach (var line in lines)
        {
            _ = MinecraftLogParser.Parse(line);
        }

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3), $"hostile log took {clock.Elapsed}");
    }
}
