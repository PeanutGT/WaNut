using WaNut.Core.Storage;
using WaNut.Core.Tests.TestSupport;
using WaNut.Core.Workspace;

namespace WaNut.Core.Tests.Workspace;

public sealed class WorkspaceBrowserTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly LocalFileSystemAdapter _fs;
    private readonly WorkspaceBrowser _browser;

    public WorkspaceBrowserTests()
    {
        _fs = new LocalFileSystemAdapter(_dir.Path);
        _browser = new WorkspaceBrowser(_fs);
    }

    public void Dispose() => _dir.Dispose();

    private string Full(string relative) => Path.Combine(_dir.Path, relative.Replace('/', Path.DirectorySeparatorChar));

    private void Write(string relative, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Full(relative))!);
        File.WriteAllText(Full(relative), content);
    }

    private async Task<ServerFileNode> NodeAsync(string path)
    {
        var slash = path.LastIndexOf('/');
        var list = await _fs.ListDirectoryAsync(slash < 0 ? "" : path[..slash]);
        return list.Single(n => n.Path == path);
    }

    [Fact]
    public async Task Measure_SplitsSizeByCategory_BiggestFirst()
    {
        Write("server.properties", new string('a', 100));
        Write("world/region/r.0.0.mca", new string('b', 5000));
        Write("logs/old.log", new string('c', 300));

        var usage = await _browser.MeasureAsync();

        Assert.Equal(5400, usage.TotalBytes);
        Assert.Equal([McFileCategory.WorldData, McFileCategory.LogOrCrash, McFileCategory.Configuration], usage.Categories.Select(c => c.Category));
        Assert.Equal(5000d / 5400, usage.Fraction(usage.Categories[0]), 6);
    }

    [Fact]
    public async Task Measure_EmptyFolder_HasNoFractions()
    {
        var usage = await _browser.MeasureAsync();

        Assert.Equal(0, usage.TotalBytes);
        Assert.Empty(usage.Categories);
    }

    [Fact]
    public async Task OpenText_ReturnsTextAndDetectsLineEndings()
    {
        Write("server.properties", "a=1\r\nb=2\r\n");

        var doc = await _browser.OpenTextAsync(await NodeAsync("server.properties"));

        Assert.Equal("a=1\r\nb=2\r\n", doc.Text);
        Assert.True(doc.CrLf);
    }

    [Theory]
    [InlineData("server.jar", TextEditProblem.NotText)]
    [InlineData("world", TextEditProblem.Folder)]
    public async Task OpenText_NotAnEditableFile_Throws(string name, TextEditProblem expected)
    {
        Write("server.jar", "x");
        Directory.CreateDirectory(Full("world"));

        var e = await Assert.ThrowsAsync<TextEditException>(async () => await _browser.OpenTextAsync(await NodeAsync(name)));

        Assert.Equal(expected, e.Problem);
    }

    [Fact]
    public async Task OpenText_TooLarge_Throws()
    {
        Write("big.json", new string('x', (int)TextFilePolicy.MaxBytes + 1));

        var e = await Assert.ThrowsAsync<TextEditException>(async () => await _browser.OpenTextAsync(await NodeAsync("big.json")));

        Assert.Equal(TextEditProblem.TooLarge, e.Problem);
    }

    [Fact]
    public async Task OpenText_BinaryContent_Throws()
    {
        Write("odd.txt", "a\0b");

        var e = await Assert.ThrowsAsync<TextEditException>(async () => await _browser.OpenTextAsync(await NodeAsync("odd.txt")));

        Assert.Equal(TextEditProblem.NotText, e.Problem);
    }

    [Fact]
    public async Task SaveText_Unchanged_WritesAndKeepsLineEndings()
    {
        Write("a.yml", "k: 1\r\n");
        var doc = await _browser.OpenTextAsync(await NodeAsync("a.yml"));

        var result = await _browser.SaveTextAsync(doc, "k: 2\nj: 3\n");

        Assert.Equal(TextSaveOutcome.Saved, result.Outcome);
        Assert.Equal("k: 2\r\nj: 3\r\n", File.ReadAllText(Full("a.yml")));
        Assert.Equal("k: 2\r\nj: 3\r\n", result.Saved!.Text);
    }

    [Fact]
    public async Task SaveText_SavedDocument_CanBeSavedAgainWithoutConflict()
    {
        Write("a.yml", "k: 1\n");
        var doc = await _browser.OpenTextAsync(await NodeAsync("a.yml"));
        var first = await _browser.SaveTextAsync(doc, "k: 2\n");

        var second = await _browser.SaveTextAsync(first.Saved!, "k: 3\n");

        Assert.Equal(TextSaveOutcome.Saved, second.Outcome);
        Assert.Equal("k: 3\n", File.ReadAllText(Full("a.yml")));
    }

    [Fact]
    public async Task SaveText_FileChangedMeanwhile_WritesNothingUnlessForced()
    {
        Write("a.yml", "k: 1\n");
        var doc = await _browser.OpenTextAsync(await NodeAsync("a.yml"));
        File.WriteAllText(Full("a.yml"), "k: other change\n");

        var refused = await _browser.SaveTextAsync(doc, "k: mine\n");
        Assert.Equal(TextSaveOutcome.ChangedMeanwhile, refused.Outcome);
        Assert.Equal("k: other change\n", File.ReadAllText(Full("a.yml")));

        var forced = await _browser.SaveTextAsync(doc, "k: mine\n", overwriteChanges: true);
        Assert.Equal(TextSaveOutcome.Saved, forced.Outcome);
        Assert.Equal("k: mine\n", File.ReadAllText(Full("a.yml")));
    }

    [Fact]
    public async Task SaveText_FileDeletedMeanwhile_IsReportedAsChanged()
    {
        Write("a.yml", "k: 1\n");
        var doc = await _browser.OpenTextAsync(await NodeAsync("a.yml"));
        File.Delete(Full("a.yml"));

        var result = await _browser.SaveTextAsync(doc, "k: 2\n");

        Assert.Equal(TextSaveOutcome.ChangedMeanwhile, result.Outcome);
    }
}
