using System.Globalization;
using System.Resources;

namespace WaNut.App.Resources;

/// <summary>
/// Looks up user-facing text in Strings.resx for the current UI culture.
/// </summary>
public static class Text
{
    private static readonly ResourceManager Manager = new("WaNut.App.Resources.Strings", typeof(Text).Assembly);

    public static string Get(string key)
    {
        // A missing key shows up as [Key] on screen instead of crashing, so it is easy to spot.
        return Manager.GetString(key, CultureInfo.CurrentUICulture) ?? $"[{key}]";
    }

    /// <summary>The text in a given language, or null when there is none (never "[Key]"): for search, which matches both languages.</summary>
    public static string? TryGet(string key, CultureInfo culture) => Manager.GetString(key, culture);
}
