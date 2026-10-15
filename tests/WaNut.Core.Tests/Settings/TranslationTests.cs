using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace WaNut.Core.Tests.Settings;

/// <summary>
/// Checks the translations against the English Strings.resx. It reads the files from the repository because the
/// resources live in the WPF project, which these tests can't reference.
/// </summary>
public partial class TranslationTests
{
    public static TheoryData<string> Translations => new() { "Strings.zh-TW.resx" };

    [Theory]
    [MemberData(nameof(Translations))]
    public void Translation_EveryEnglishKey_HasText(string file)
    {
        var english = Load("Strings.resx");
        var translated = Load(file);

        var missing = english.Keys.Where(k => string.IsNullOrWhiteSpace(translated.GetValueOrDefault(k))).ToList();
        var extra = translated.Keys.Except(english.Keys).ToList();

        Assert.True(missing.Count == 0, $"Missing in {file}: {string.Join(", ", missing)}");
        Assert.True(extra.Count == 0, $"Not in Strings.resx: {string.Join(", ", extra)}");
    }

    [Theory]
    [MemberData(nameof(Translations))]
    public void Translation_Placeholders_MatchEnglish(string file)
    {
        // A dropped {0} hides information, and an extra {1} makes string.Format throw at run time.
        var english = Load("Strings.resx");
        var translated = Load(file);

        var wrong = english
            .Where(e => translated.ContainsKey(e.Key) && Placeholders(e.Value) != Placeholders(translated[e.Key]))
            .Select(e => e.Key)
            .ToList();

        Assert.True(wrong.Count == 0, $"Placeholders differ in {file}: {string.Join(", ", wrong)}");
    }

    private static string Placeholders(string text) =>
        string.Join(",", PlaceholderRegex().Matches(text).Select(m => m.Groups[1].Value).Distinct().Order());

    private static Dictionary<string, string> Load(string file)
    {
        var path = Path.Combine(RepositoryRoot(), "src", "WaNut.App", "Resources", file);
        return XDocument.Load(path).Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? "");
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

    [GeneratedRegex(@"\{(\d+)[^}]*\}")]
    private static partial Regex PlaceholderRegex();
}
