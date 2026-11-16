using Microsoft.Extensions.Time.Testing;
using WaNut.Core.Diagnostics;
using WaNut.Core.Tests.TestSupport;

namespace WaNut.Core.Tests.Diagnostics;

public sealed class ErrorLogTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private string LogFile => Path.Combine(_dir.Path, "logs", "errors.log");

    [Fact]
    public void Write_NewFile_CreatesFolderAndKeepsTimeSourceAndError()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 16, 8, 9, 10, TimeSpan.Zero));
        var log = new ErrorLog(LogFile, time);

        Assert.True(log.Write("ui", new InvalidOperationException("boom")));

        var text = File.ReadAllText(LogFile);
        Assert.StartsWith("[2026-11-16 08:09:10Z] ui", text, StringComparison.Ordinal);
        Assert.Contains("System.InvalidOperationException: boom", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_Twice_KeepsBoth()
    {
        var log = new ErrorLog(LogFile);

        log.Write("ui", new InvalidOperationException("first"));
        log.Write("task", new IOException("second"));

        var text = File.ReadAllText(LogFile);
        Assert.Contains("first", text, StringComparison.Ordinal);
        Assert.Contains("second", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_FileOverTheLimit_StartsANewOneAndKeepsTheOld()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LogFile)!);
        File.WriteAllText(LogFile, new string('x', (int)ErrorLog.MaxBytes + 1));
        var log = new ErrorLog(LogFile);

        log.Write("ui", new InvalidOperationException("fresh"));

        Assert.True(new FileInfo(LogFile).Length < ErrorLog.MaxBytes);
        Assert.Contains("fresh", File.ReadAllText(LogFile), StringComparison.Ordinal);
        Assert.True(new FileInfo(LogFile + ".old").Length > ErrorLog.MaxBytes);
    }

    [Fact]
    public void Write_PathIsAFolder_ReturnsFalseInsteadOfThrowing()
    {
        Directory.CreateDirectory(LogFile);
        var log = new ErrorLog(LogFile);

        Assert.False(log.Write("ui", new InvalidOperationException("boom")));
    }
}
