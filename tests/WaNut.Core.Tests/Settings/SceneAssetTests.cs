using System.Text.RegularExpressions;
using WaNut.Core.Instances;

namespace WaNut.Core.Tests.Settings;

/// <summary>
/// Checks the peanut pictures in the WPF project's Assets/Scenes. A scene the app asks for but can't find crashes it,
/// as the create wizard did when the Forge, NeoForge and modpack icons came without the frames the app expected.
/// </summary>
public partial class SceneAssetTests
{
    private static readonly string ScenesDir = Path.Combine(RepositoryRoot(), "src", "WaNut.App", "Assets", "Scenes");

    [Fact]
    public void Scenes_EachHasOneFrameOrAllFour()
    {
        var wrong = Directory.GetFiles(ScenesDir, "*-32.png")
            .Select(Path.GetFileNameWithoutExtension)
            .Select(f => FrameSuffixRegex().Replace(f!, ""))
            .Distinct()
            .Where(s => !File.Exists(Frame(s, 1)) || Enumerable.Range(2, 3).Count(k => File.Exists(Frame(s, k))) is not (0 or 3))
            .ToList();

        Assert.True(wrong.Count == 0, $"Scenes with missing frames: {string.Join(", ", wrong)}");
    }

    [Fact]
    public void Scenes_EveryServerTypeHasAnIcon()
    {
        var missing = Enum.GetValues<ServerType>()
            .Select(t => $"type-{t.ToString().ToLowerInvariant()}")
            .Where(s => !File.Exists(Frame(s, 1)))
            .ToList();

        Assert.True(missing.Count == 0, $"No icon for: {string.Join(", ", missing)}");
    }

    [Fact]
    public void Scenes_NamedInXaml_Exist()
    {
        var views = Path.Combine(RepositoryRoot(), "src", "WaNut.App", "Views");
        var missing = Directory.GetFiles(views, "*.xaml")
            .SelectMany(f => SceneRegex().Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value))
            .Distinct()
            .Where(s => !File.Exists(Frame(s, 1)))
            .ToList();

        Assert.True(missing.Count == 0, $"Not in Assets/Scenes: {string.Join(", ", missing)}");
    }

    private static string Frame(string scene, int k) =>
        Path.Combine(ScenesDir, k == 1 ? $"{scene}-32.png" : $"{scene}-f{k}-32.png");

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WaNut.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("WaNut.slnx not found above the test folder.");
    }

    [GeneratedRegex(@"-f\d-32$|-32$")]
    private static partial Regex FrameSuffixRegex();

    [GeneratedRegex(@"\bScene=""([a-z0-9-]+)""")]
    private static partial Regex SceneRegex();
}
