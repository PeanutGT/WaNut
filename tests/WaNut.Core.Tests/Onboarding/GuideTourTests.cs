using System.Xml.Linq;
using WaNut.Core.Onboarding;

namespace WaNut.Core.Tests.Onboarding;

public class GuideTourTests
{
    [Fact]
    public void Plan_FirstRunWithoutServer_ShowsIntroThenLater()
    {
        var plan = GuideTour.Plan(introDone: false, tabsDone: false, hasServer: false);

        Assert.Equal([.. GuideTour.Intro, GuideTour.Later], plan);
        Assert.False(GuideTour.NeedsServer(plan));
    }

    [Fact]
    public void Plan_FirstRunWithServer_ShowsWholeTour()
    {
        var plan = GuideTour.Plan(introDone: false, tabsDone: false, hasServer: true);

        Assert.Equal([.. GuideTour.Intro, .. GuideTour.Tabs, GuideTour.Done], plan);
        Assert.True(GuideTour.NeedsServer(plan));
    }

    [Fact]
    public void Plan_IntroDoneAndFirstServer_ShowsTabs()
    {
        var plan = GuideTour.Plan(introDone: true, tabsDone: false, hasServer: true);

        Assert.Equal([.. GuideTour.Tabs, GuideTour.Done], plan);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void Plan_NothingLeftForNow_IsEmpty(bool introDone, bool tabsDone, bool hasServer)
    {
        Assert.Empty(GuideTour.Plan(introDone, tabsDone, hasServer));
    }

    [Fact]
    public void Plan_TabsDoneButIntroNot_EndsWithDone()
    {
        var plan = GuideTour.Plan(introDone: false, tabsDone: true, hasServer: true);

        Assert.Equal([.. GuideTour.Intro, GuideTour.Done], plan);
    }

    [Fact]
    public void Tabs_CoverEveryTabInOrder()
    {
        var tabs = GuideTour.Tabs.Where(s => s.Target == GuideTarget.Tab).Select(s => s.TabIndex);

        Assert.Equal(Enumerable.Range(0, 7), tabs);
    }

    [Theory]
    [InlineData("Strings.resx")]
    [InlineData("Strings.zh-TW.resx")]
    public void All_EveryStop_HasTitleAndText(string file)
    {
        var keys = XDocument.Load(Path.Combine(RepositoryRoot(), "src", "WaNut.App", "Resources", file)).Root!
            .Elements("data").Select(d => (string)d.Attribute("name")!).ToHashSet();

        var missing = GuideTour.All
            .SelectMany(s => new[] { $"Guide_{s.Id}Title", $"Guide_{s.Id}Text" })
            .Where(k => !keys.Contains(k))
            .ToList();

        Assert.True(missing.Count == 0, $"Missing in {file}: {string.Join(", ", missing)}");
    }

    [Fact]
    public void All_EveryStop_HasItsPicture()
    {
        var scenes = Path.Combine(RepositoryRoot(), "src", "WaNut.App", "Assets", "Scenes");

        var missing = GuideTour.All.Select(s => s.Scene).Distinct()
            .Where(s => !File.Exists(Path.Combine(scenes, $"{s}-32.png")))
            .ToList();

        Assert.True(missing.Count == 0, $"Not in Assets/Scenes: {string.Join(", ", missing)}");
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WaNut.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("WaNut.slnx not found above the test folder.");
    }
}
