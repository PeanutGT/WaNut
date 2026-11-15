using System.Globalization;
using System.Text.RegularExpressions;

namespace WaNut.Core.Tests.Xaml;

/// <summary>
/// Keeps the screens on the rules in docs/design.md: theme brushes only, spacing on the 3px scale, text sizes from
/// Themes/Text.xaml, and pixel art at 3x. Reads the WPF project's XAML from the repository because these tests
/// can't reference it.
/// </summary>
public partial class DesignRulesTests
{
    private static readonly double[] SpacingScale = [0, 3, 6, 12, 18, 24, 36];

    // One image pixel is 3px on screen: 16, 24 and 32 pixel art.
    private static readonly string[] PixelArtSizes = ["48", "72", "96"];

    /// <summary>
    /// Files that still break a rule. Fix a file, then remove it here; a file listed without a violation fails the
    /// test so the list can only shrink.
    /// </summary>
    private static readonly Dictionary<string, string[]> NotYetFixed = new()
    {
        ["color"] = [],
        ["spacing"] = [],
        ["text size"] = [],
        ["pixel art size"] = [],
    };

    [Fact]
    public void Colors_ComeFromThemeBrushes()
    {
        AssertRule("color", xaml => HexColor().Matches(xaml).Select(m => m.Value));
    }

    [Fact]
    public void MarginsAndPaddings_AreOnTheSpacingScale()
    {
        AssertRule("spacing", xaml => Spacing().Matches(xaml)
            .Where(m => m.Groups[1].Value
                .Split([',', ' '], StringSplitOptions.RemoveEmptyEntries)
                .Any(n => !SpacingScale.Contains(Math.Abs(double.Parse(n, CultureInfo.InvariantCulture)))))
            .Select(m => m.Value));
    }

    [Fact]
    public void TextSizes_ComeFromTheTextScale()
    {
        AssertRule("text size", xaml => LiteralFontSize().Matches(xaml).Select(m => m.Value));
    }

    [Fact]
    public void PixelArt_IsShownAtThreeTimes()
    {
        AssertRule("pixel art size", xaml => PixelArtElement().Matches(xaml)
            .Select(m => m.Value)
            .Where(tag => Size().Matches(tag).Any(s => !PixelArtSizes.Contains(s.Groups[1].Value))));
    }

    private static void AssertRule(string rule, Func<string, IEnumerable<string>> violations)
    {
        var problems = new List<string>();
        foreach (var file in ScreenFiles())
        {
            var name = Path.GetFileName(file);
            var found = violations(File.ReadAllText(file)).ToList();
            var excused = NotYetFixed[rule].Contains(name);
            if (found.Count > 0 && !excused)
            {
                problems.Add($"{name}: {string.Join(" | ", found.Take(5))}{(found.Count > 5 ? $" (+{found.Count - 5} more)" : "")}");
            }
            else if (found.Count == 0 && excused)
            {
                problems.Add($"{name}: follows the rule now, remove it from NotYetFixed[\"{rule}\"]");
            }
        }

        Assert.True(problems.Count == 0, $"docs/design.md, {rule}:{Environment.NewLine}{string.Join(Environment.NewLine, problems)}");
    }

    /// <summary>Every screen and the shared control styles. The theme files are where colors and sizes are defined.</summary>
    private static IEnumerable<string> ScreenFiles()
    {
        var app = Path.Combine(RepositoryRoot(), "src", "WaNut.App");
        return Directory.GetFiles(Path.Combine(app, "Views"), "*.xaml")
            .Append(Path.Combine(app, "Themes", "Controls.xaml"));
    }

    [GeneratedRegex(@"=""#[0-9A-Fa-f]{3,8}""")]
    private static partial Regex HexColor();

    [GeneratedRegex(@"(?:\b(?:Margin|Padding)=""|Property=""(?:Margin|Padding)"" Value="")([-\d., ]+)""")]
    private static partial Regex Spacing();

    [GeneratedRegex(@"(?:\bFontSize=""|Property=""FontSize"" Value="")[\d.]+""")]
    private static partial Regex LiteralFontSize();

    // The peanut and the scenes: <Image Style="{StaticResource Peanut}" ...> and <controls:PixelScene ...>.
    [GeneratedRegex(@"<(?:controls:PixelScene\b|Image\b[^>]*\{StaticResource (?:Busy)?Peanut\})[^>]*>")]
    private static partial Regex PixelArtElement();

    [GeneratedRegex(@"\b(?:Width|Height)=""([^""]+)""")]
    private static partial Regex Size();

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
