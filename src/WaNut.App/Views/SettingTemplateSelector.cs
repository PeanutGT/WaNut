using System.Windows;
using System.Windows.Controls;
using WaNut.App.ViewModels;
using WaNut.Core.Settings.Config;

namespace WaNut.App.Views;

/// <summary>Picks the control of a setting from its <see cref="ConfigControlType"/>.</summary>
public sealed class SettingTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Toggle { get; set; }

    public DataTemplate? Dropdown { get; set; }

    public DataTemplate? Slider { get; set; }

    public DataTemplate? Number { get; set; }

    public DataTemplate? Text { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) =>
        item is SettingEntryViewModel entry
            ? entry.ControlType switch
            {
                ConfigControlType.Toggle => Toggle,
                ConfigControlType.Dropdown => Dropdown,
                ConfigControlType.Slider => Slider,
                ConfigControlType.NumberInput => Number,
                _ => Text,
            }
            : null;
}
