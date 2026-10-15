namespace WaNut.Core.Settings;

/// <summary>The UI languages WaNut ships, as culture names.</summary>
public static class UiLanguages
{
    public const string English = "en";
    public const string TraditionalChinese = "zh-TW";

    public static IReadOnlyList<string> All { get; } = [English, TraditionalChinese];

    /// <summary>
    /// The language to show for a saved setting. Anything missing or unknown becomes English, so a hand-edited
    /// config.json can't leave the UI in a language WaNut has no text for.
    /// </summary>
    public static string Resolve(string? saved) =>
        All.FirstOrDefault(l => string.Equals(l, saved, StringComparison.OrdinalIgnoreCase)) ?? English;
}
