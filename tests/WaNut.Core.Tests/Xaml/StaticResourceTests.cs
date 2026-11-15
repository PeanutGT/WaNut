using System.Text.RegularExpressions;

namespace WaNut.Core.Tests.Xaml;

/// <summary>
/// A StaticResource key that a window can't find throws while the window loads, so the app closes. The resource
/// keys are local to each XAML file plus the shared Themes dictionaries; this reads the WPF project's files from the
/// repository because these tests can't reference it.
/// </summary>
public partial class StaticResourceTests
{
    [Fact]
    public void EveryStaticResource_IsDefinedInItsFileOrTheThemes()
    {
        var app = Path.Combine(RepositoryRoot(), "src", "WaNut.App");
        var shared = Directory.GetFiles(Path.Combine(app, "Themes"), "*.xaml")
            .Append(Path.Combine(app, "App.xaml"))
            .SelectMany(f => Keys(File.ReadAllText(f)))
            .ToHashSet();

        var missing = Directory.GetFiles(Path.Combine(app, "Views"), "*.xaml")
            .SelectMany(file =>
            {
                var text = File.ReadAllText(file);
                var local = Keys(text).ToHashSet();
                return UsedKeys().Matches(text)
                    .Select(m => m.Groups[1].Value)
                    .Where(k => !local.Contains(k) && !shared.Contains(k))
                    .Select(k => $"{Path.GetFileName(file)}: {k}");
            })
            .Distinct()
            .ToList();

        Assert.True(missing.Count == 0, $"Unknown StaticResource keys: {string.Join(", ", missing)}");
    }

    private static IEnumerable<string> Keys(string xaml) => DefinedKeys().Matches(xaml).Select(m => m.Groups[1].Value);

    [GeneratedRegex(@"x:Key=""([^""]+)""")]
    private static partial Regex DefinedKeys();

    [GeneratedRegex(@"\{StaticResource\s+([A-Za-z0-9_.]+)\}")]
    private static partial Regex UsedKeys();

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
