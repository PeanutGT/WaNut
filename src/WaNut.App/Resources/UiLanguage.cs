using System.Globalization;
using WaNut.Core.Settings;

namespace WaNut.App.Resources;

/// <summary>Sets the language WaNut's text is shown in.</summary>
public static class UiLanguage
{
    /// <summary>
    /// Must run before any window opens. English is set explicitly, otherwise a Traditional Chinese Windows would
    /// pick up Strings.zh-TW.resx on its own. Only the UI culture changes; number and date formats follow Windows.
    /// </summary>
    public static void Apply(string? saved)
    {
        var culture = CultureInfo.GetCultureInfo(UiLanguages.Resolve(saved));
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
}
