using WaNut.Core.Settings;

namespace WaNut.Core.Tests.Settings;

public class UiLanguagesTests
{
    [Theory]
    [InlineData(null, "en")]
    [InlineData("", "en")]
    [InlineData("fr", "en")]
    [InlineData("en", "en")]
    [InlineData("zh-TW", "zh-TW")]
    [InlineData("zh-tw", "zh-TW")]
    public void Resolve_SavedValue_ReturnsShippedLanguage(string? saved, string expected)
    {
        Assert.Equal(expected, UiLanguages.Resolve(saved));
    }
}
