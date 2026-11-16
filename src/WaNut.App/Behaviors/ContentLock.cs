using System.Windows;
using System.Windows.Controls;

namespace WaNut.App.Behaviors;

/// <summary>
/// Locks a tab's controls but not its scrolling. A disabled ScrollViewer ignores the mouse wheel and its scroll bar,
/// so setting IsEnabled on a whole tab also stops the user from reading it. This disables what the tab's top
/// ScrollViewer holds instead, or the whole tab when it has none.
/// Usage: &lt;views:ModsView behaviors:ContentLock.IsUnlocked="{Binding CanEditFiles}" /&gt;
/// </summary>
public static class ContentLock
{
    public static readonly DependencyProperty IsUnlockedProperty = DependencyProperty.RegisterAttached(
        "IsUnlocked", typeof(bool), typeof(ContentLock), new PropertyMetadata(true, OnIsUnlockedChanged));

    public static bool GetIsUnlocked(DependencyObject element) => (bool)element.GetValue(IsUnlockedProperty);

    public static void SetIsUnlocked(DependencyObject element, bool value) => element.SetValue(IsUnlockedProperty, value);

    private static void OnIsUnlockedChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        var unlocked = (bool)e.NewValue;
        var target = element is UserControl { Content: ScrollViewer { Content: UIElement content } } ? content : element as UIElement;
        target?.SetCurrentValue(UIElement.IsEnabledProperty, unlocked);
    }
}
