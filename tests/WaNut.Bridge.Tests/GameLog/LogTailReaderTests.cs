using System.Text;
using WaNut.Bridge.GameLog;

namespace WaNut.Bridge.Tests.GameLog;

public sealed class LogTailReaderTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("wanut-tail-").FullName;

    private string LogPath => Path.Combine(_folder, "latest.log");

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public async Task PollAsync_AfterSkipToEnd_ReturnsOnlyNewLines()
    {
        File.WriteAllText(LogPath, "[10:00:00] [main/INFO]: old line\n");
        var reader = new LogTailReader(LogPath);
        await reader.SkipToEndAsync();

        File.AppendAllText(LogPath, "new one\nnew two\n");

        Assert.Equal(["new one", "new two"], await reader.PollAsync());
        Assert.Empty(await reader.PollAsync());
    }

    [Fact]
    public async Task PollAsync_LineWithoutNewlineYet_WaitsForTheRest()
    {
        File.WriteAllText(LogPath, "head\n");
        var reader = new LogTailReader(LogPath);
        await reader.SkipToEndAsync();

        File.AppendAllText(LogPath, "half a li");
        Assert.Empty(await reader.PollAsync());
        File.AppendAllText(LogPath, "ne\r\n");

        Assert.Equal(["half a line"], await reader.PollAsync());
    }

    [Fact]
    public async Task PollAsync_MultiByteCharacterSplitAcrossPolls_DecodesWhole()
    {
        File.WriteAllText(LogPath, "head\n");
        var reader = new LogTailReader(LogPath);
        await reader.SkipToEndAsync();
        var bytes = Encoding.UTF8.GetBytes("<小明> 你好\n");

        await AppendBytesAsync(bytes[..3]);
        Assert.Empty(await reader.PollAsync());
        await AppendBytesAsync(bytes[3..]);

        Assert.Equal(["<小明> 你好"], await reader.PollAsync());
    }

    [Fact]
    public async Task PollAsync_FileReplacedByShorterOne_ReadsNewFileFromStart()
    {
        File.WriteAllText(LogPath, "[10:00:00] [main/INFO]: first run\n" + new string('x', 500) + "\n");
        var reader = new LogTailReader(LogPath);
        await reader.SkipToEndAsync();

        File.Delete(LogPath);
        File.WriteAllText(LogPath, "[11:00:00] [main/INFO]: second run\n");

        Assert.Equal(["[11:00:00] [main/INFO]: second run"], await reader.PollAsync());
    }

    [Fact]
    public async Task PollAsync_FileReplacedByLongerOne_SeesNewHeadAndReadsFromStart()
    {
        File.WriteAllText(LogPath, "[10:00:00] [main/INFO]: first run\n");
        var reader = new LogTailReader(LogPath);
        await reader.SkipToEndAsync();

        var second = "[11:00:00] [main/INFO]: second run\n" + string.Concat(Enumerable.Repeat("more lines here\n", 20));
        File.Move(LogPath, Path.Combine(_folder, "2026-11-17-1.log"));
        File.WriteAllText(LogPath, second);

        var lines = await reader.PollAsync();

        Assert.Equal(21, lines.Count);
        Assert.Equal("[11:00:00] [main/INFO]: second run", lines[0]);
    }

    [Fact]
    public async Task PollAsync_YoungFileGrowsPastFingerprint_IsNotTreatedAsNew()
    {
        File.WriteAllText(LogPath, "short\n");
        var reader = new LogTailReader(LogPath);
        await reader.SkipToEndAsync();

        File.AppendAllText(LogPath, new string('y', LogTailReader.FingerprintLength) + "\n");

        Assert.Equal([new string('y', LogTailReader.FingerprintLength)], await reader.PollAsync());
    }

    [Fact]
    public async Task PollAsync_FileMissingThenCreated_ReadsItFromStart()
    {
        var reader = new LogTailReader(LogPath);
        await reader.SkipToEndAsync();
        Assert.Empty(await reader.PollAsync());

        File.WriteAllText(LogPath, "first\n");

        Assert.Equal(["first"], await reader.PollAsync());
    }

    [Fact]
    public async Task PollAsync_WhileWriterHoldsTheFile_StillReadsAndWriterCanRename()
    {
        await using (var writer = new FileStream(LogPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
        {
            var reader = new LogTailReader(LogPath);
            await reader.SkipToEndAsync();
            await writer.WriteAsync(Encoding.UTF8.GetBytes("written while open\n"));
            await writer.FlushAsync();

            Assert.Equal(["written while open"], await reader.PollAsync());
        }

        File.Move(LogPath, Path.Combine(_folder, "old.log"));
    }

    private async Task AppendBytesAsync(byte[] bytes)
    {
        await using var stream = new FileStream(LogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        await stream.WriteAsync(bytes);
    }
}
