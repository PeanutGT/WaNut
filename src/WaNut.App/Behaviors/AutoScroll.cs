using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WaNut.App.Behaviors;

/// <summary>
/// Keeps a list scrolled to its newest item, like a terminal, unless the user has scrolled up to read.
/// Usage: &lt;ListBox behaviors:AutoScroll.IsEnabled="True" /&gt;
/// </summary>
public static class AutoScroll
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(AutoScroll), new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not ItemsControl list || e.NewValue is not true)
        {
            return;
        }

        // Items follows ItemsSource, so this keeps working when another server is selected.
        ((INotifyCollectionChanged)list.Items).CollectionChanged += (_, args) =>
        {
            if (args.Action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Reset)
            {
                ScrollToEndIfFollowing(list);
            }
        };
    }

    private static void ScrollToEndIfFollowing(ItemsControl list)
    {
        if (FindScrollViewer(list) is not { } viewer)
        {
            return;
        }

        // Checked before the new item is laid out, so "at the end" means the user was following along.
        if (viewer.VerticalOffset >= viewer.ScrollableHeight - 1)
        {
            viewer.ScrollToEnd();
        }
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollViewer viewer)
            {
                return viewer;
            }

            if (FindScrollViewer(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }
}
