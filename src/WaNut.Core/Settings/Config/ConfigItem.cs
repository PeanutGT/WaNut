using System.ComponentModel;
using System.Globalization;

namespace WaNut.Core.Settings.Config;

/// <summary>
/// One setting as the UI sees it. Every view of the setting (graphic control, full property table) binds to the
/// same instance, and every typed property reads and writes the one string in the shared store, so views cannot
/// drift apart.
/// </summary>
public sealed class ConfigItem : INotifyPropertyChanged
{
    private readonly IDictionary<string, string> _store;

    public ConfigItem(ConfigMetadata meta, IDictionary<string, string> store)
    {
        Meta = meta;
        _store = store;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ConfigMetadata Meta { get; }

    /// <summary>The value as written in the file; the default when the file has no such key.</summary>
    public string RawValue
    {
        get => _store.TryGetValue(Meta.Key, out var value) ? value : Meta.DefaultValue;
        set
        {
            if (string.Equals(RawValue, value, StringComparison.Ordinal))
            {
                return;
            }

            _store[Meta.Key] = value;
            NotifyValueChanged();
        }
    }

    /// <summary>Like Java: anything but "true" (any case) is false.</summary>
    public bool BoolValue
    {
        get => bool.TryParse(RawValue.Trim(), out var result) && result;
        set => RawValue = value ? "true" : "false";
    }

    /// <summary>The value as a number; an unreadable value shows as the default. Writes are rounded and kept within Min and Max.</summary>
    public double NumericValue
    {
        get => TryParse(RawValue, out var result) ? result
            : TryParse(Meta.DefaultValue, out var fallback) ? fallback
            : Meta.Min;
        set => RawValue = Math.Clamp(Math.Round(value), Meta.Min, Meta.Max).ToString("F0", CultureInfo.InvariantCulture);
    }

    public bool IsModified => !string.Equals(RawValue, Meta.DefaultValue, StringComparison.OrdinalIgnoreCase);

    /// <summary>True while the inline risk banner (for example online-mode = false) should be open.</summary>
    public bool ShouldShowWarning =>
        !string.IsNullOrEmpty(Meta.WarningBannerKey)
        && string.Equals(RawValue.Trim(), Meta.WarningTriggerValue, StringComparison.OrdinalIgnoreCase);

    /// <summary>True while the gentle hint (for example a very high view distance) should be open.</summary>
    public bool ShouldShowHint => !string.IsNullOrEmpty(Meta.HintKey) && NumericValue > Meta.HintAbove;

    public void ResetToDefault() => RawValue = Meta.DefaultValue;

    private static bool TryParse(string text, out double result) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out result) && double.IsFinite(result);

    private void NotifyValueChanged()
    {
        foreach (var name in new[] { nameof(RawValue), nameof(BoolValue), nameof(NumericValue), nameof(IsModified), nameof(ShouldShowWarning), nameof(ShouldShowHint) })
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
