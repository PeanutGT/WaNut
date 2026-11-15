using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace WaNut.App.Behaviors;

/// <summary>
/// Keeps a list scrolled to its newest item, like a terminal, unless the user has scrolled up to read.
/// Usage: &lt;ListBox behaviors:AutoScroll.IsEnabled="True" /&gt;
/// </summary>
public static class AutoScroll
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(AutoScroll), new PropertyMetadata(false, OnIsEnabledChanged));

    // Per scroll viewer: true while the user is following the newest line.
    private static readonly DependencyProperty IsFollowingProperty = DependencyProperty.RegisterAttached(
        "IsFollowing", typeof(bool), typeof(AutoScroll), new PropertyMetadata(true));

    // Per list: true while a scroll to the end waits to run.
    private static readonly DependencyProperty IsScrollQueuedProperty = DependencyProperty.RegisterAttached(
        "IsScrollQueued", typeof(bool), typeof(AutoScroll), new PropertyMetadata(false));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not ItemsControl list || e.NewValue is not true)
        {
            return;
        }

        // ScrollChanged bubbles up from the list's own scroll viewer.
        list.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnScrollChanged));

        // Items follows ItemsSource, so this keeps working when another server is selected.
        ((INotifyCollectionChanged)list.Items).CollectionChanged += (_, args) =>
        {
            // A batch of new lines comes as one Add each: scroll once, after the whole batch is in.
            if (args.Action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Reset
                && !(bool)list.GetValue(IsScrollQueuedProperty))
            {
                list.SetValue(IsScrollQueuedProperty, true);
                list.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
                {
                    list.SetValue(IsScrollQueuedProperty, false);
                    ScrollToEndIfFollowing(list);
                });
            }
        };
    }

    private static void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.OriginalSource is not ScrollViewer viewer)
        {
            return;
        }

        // Only a scroll that did not come from new content (the user, or a resize) changes whether we follow.
        // Lines can arrive faster than layout runs, so checking the offset when a line is added would see a
        // stale position and stop following for good.
        if (e.ExtentHeightChange == 0)
        {
            viewer.SetValue(IsFollowingProperty, viewer.VerticalOffset >= viewer.ScrollableHeight - 1);
        }
    }

    private static void ScrollToEndIfFollowing(ItemsControl list)
    {
        if (FindScrollViewer(list) is { } viewer && (bool)viewer.GetValue(IsFollowingProperty))
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
