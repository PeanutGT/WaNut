using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WaNut.App.Resources;
using WaNut.Core.Settings.Config;

namespace WaNut.App.ViewModels;

/// <summary>
/// One setting on the Game settings page. It wraps the <see cref="ConfigItem"/> that holds the value, so the
/// graphic control and the line in the full property list edit the same value and stay in step.
/// </summary>
public sealed partial class SettingEntryViewModel : ObservableObject
{
    private static readonly HashSet<string> CatalogKeys = ServerConfigCatalog.All.Select(m => m.Key).ToHashSet();

    private static readonly CultureInfo English = CultureInfo.InvariantCulture;
    private static readonly CultureInfo Chinese = CultureInfo.GetCultureInfo("zh-TW");

    private readonly Lazy<string[]> _searchText;

    public SettingEntryViewModel(ConfigItem item)
    {
        Item = item;
        Title = Text.TryGet(item.Meta.TitleKey, CultureInfo.CurrentUICulture) ?? item.Meta.Key;
        Description = Text.TryGet(item.Meta.DescriptionKey, CultureInfo.CurrentUICulture) ?? string.Empty;
        WarningText = item.Meta.WarningBannerKey is { } warning ? Text.TryGet(warning, CultureInfo.CurrentUICulture) ?? string.Empty : string.Empty;
        HintText = item.Meta.HintKey is { } hint ? Text.TryGet(hint, CultureInfo.CurrentUICulture) ?? string.Empty : string.Empty;
        // A key only a mod knows has no default of ours to go back to.
        ShowResetTools = CatalogKeys.Contains(item.Meta.Key);
        Options = item.Meta.Options?.Select(o => new Option<string>(o.Value, Text.Get(o.LocalizationKey))).ToList() ?? [];
        _searchText = new Lazy<string[]>(() =>
        [
            Title,
            Description,
            item.Meta.Key,
            Text.TryGet(item.Meta.TitleKey, English) ?? string.Empty,
            Text.TryGet(item.Meta.TitleKey, Chinese) ?? string.Empty,
            Text.TryGet(item.Meta.DescriptionKey, English) ?? string.Empty,
            Text.TryGet(item.Meta.DescriptionKey, Chinese) ?? string.Empty,
        ]);
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConfigItem.RawValue))
            {
                OnPropertyChanged(nameof(NumberText));
                ResetCommand.NotifyCanExecuteChanged();
            }
        };
    }

    public ConfigItem Item { get; }

    /// <summary>The file key, shown in the full property list.</summary>
    public string Key => Item.Meta.Key;

    public string Title { get; }

    public string Description { get; }

    /// <summary>Says why the value is risky (the red banner); empty when the setting has no such banner.</summary>
    public string WarningText { get; }

    /// <summary>The gentle note for a very high number; empty when the setting has none.</summary>
    public string HintText { get; }

    /// <summary>False for a key only a mod knows: the dot and the reset button have nothing to go back to.</summary>
    public bool ShowResetTools { get; }

    public bool HasDescription => Description.Length > 0;

    public ConfigControlType ControlType => Item.Meta.ControlType;

    public IReadOnlyList<Option<string>> Options { get; }

    public double Min => Item.Meta.Min;

    public double Max => Item.Meta.Max;

    /// <summary>False when the value cannot change now, such as a file key while the server runs.</summary>
    [ObservableProperty]
    private bool _isEnabled = true;

    partial void OnIsEnabledChanged(bool value) => ResetCommand.NotifyCanExecuteChanged();

    /// <summary>Puts the default back. Locked together with the setting itself.</summary>
    [RelayCommand(CanExecute = nameof(CanReset))]
    private void Reset() => Item.ResetToDefault();

    private bool CanReset() => IsEnabled && Item.IsModified;

    /// <summary>False while a search is on and this setting does not match it.</summary>
    [ObservableProperty]
    private bool _isVisible = true;

    /// <summary>Says why the setting is locked, such as "the world already exists"; empty when there is nothing to say.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNote))]
    private string _note = string.Empty;

    public bool HasNote => Note.Length > 0;

    /// <summary>
    /// The number as typed. A value that is not a number is dropped and the box shows the saved one again; a number
    /// outside the range is kept within it (by <see cref="ConfigItem.NumericValue"/>), and the box shows that.
    /// </summary>
    public string NumberText
    {
        get => Item.RawValue;
        set
        {
            if (double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number))
            {
                Item.NumericValue = number;
            }

            OnPropertyChanged();
        }
    }

    /// <summary>Matches the English name, the Traditional Chinese name, the descriptions and the file key, in any case.</summary>
    public bool Matches(string query) =>
        _searchText.Value.Any(part => part.Contains(query, StringComparison.CurrentCultureIgnoreCase));
}

/// <summary>A collapsible group of settings, or the full property list.</summary>
public sealed partial class SettingsGroup(string title) : ObservableObject
{
    public string Title { get; } = title;

    public ObservableCollection<SettingEntryViewModel> Entries { get; } = [];

    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>True while at least one setting of the group passes the search; always true without a search.</summary>
    [ObservableProperty]
    private bool _isShown = true;

    /// <summary>Whether the user had it open before a search began; put back when the search is cleared.</summary>
    internal bool ExpandedBeforeSearch { get; set; }
}
