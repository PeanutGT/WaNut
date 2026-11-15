using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WaNut.App.Services;

namespace WaNut.App.Behaviors;

/// <summary>
/// Moves the tab control's selection line (the template part named "Indicator") under the selected tab, sliding
/// when the selection changes. Usage: &lt;Setter Property="behaviors:TabIndicator.IsEnabled" Value="True" /&gt;
/// </summary>
public static class TabIndicator
{
    private const string IndicatorPart = "Indicator";

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(TabIndicator), new PropertyMetadata(false, OnIsEnabledChanged));

    // Per tab control: where the line was last put, so layout passes that change nothing cost nothing.
    private static readonly DependencyProperty PlaceProperty = DependencyProperty.RegisterAttached(
        "Place", typeof(Rect), typeof(TabIndicator), new PropertyMetadata(Rect.Empty));

    // Per tab control: true from a selection change until the next layout pass, which then slides instead of jumping.
    private static readonly DependencyProperty SlidesNextProperty = DependencyProperty.RegisterAttached(
        "SlidesNext", typeof(bool), typeof(TabIndicator), new PropertyMetadata(false));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TabControl tabs || e.NewValue is not true)
        {
            return;
        }

        tabs.SelectionChanged += (_, args) =>
        {
            // Lists inside a tab raise this event too.
            if (ReferenceEquals(args.OriginalSource, tabs))
            {
                tabs.SetValue(SlidesNextProperty, true);
                tabs.InvalidateArrange();
            }
        };

        // Tabs move without a selection change too: a resize, a tab that appears, a header whose text changes.
        tabs.LayoutUpdated += (_, _) => Place(tabs);
    }

    private static void Place(TabControl tabs)
    {
        if (tabs.Template?.FindName(IndicatorPart, tabs) is not FrameworkElement line
            || VisualTreeHelper.GetParent(line) is not UIElement host)
        {
            return;
        }

        if (tabs.ItemContainerGenerator.ContainerFromIndex(tabs.SelectedIndex) is not TabItem { IsVisible: true } tab
            || !tab.IsDescendantOf(host))
        {
            line.Visibility = Visibility.Hidden;
            tabs.SetValue(PlaceProperty, Rect.Empty);
            return;
        }

        var corner = tab.TransformToAncestor(host).Transform(new Point(0, tab.ActualHeight - line.Height));
        var target = new Rect(corner.X, corner.Y, tab.ActualWidth, line.Height);
        var last = (Rect)tabs.GetValue(PlaceProperty);
        if (target == last)
        {
            return;
        }

        var slides = (bool)tabs.GetValue(SlidesNextProperty) && MotionSettings.IsOn
            // Only along a row: a line flying diagonally to a wrapped row reads as a glitch.
            && !last.IsEmpty && last.Y == target.Y;
        tabs.SetValue(SlidesNextProperty, false);
        tabs.SetValue(PlaceProperty, target);
        line.Visibility = Visibility.Visible;

        if (line.RenderTransform is not TranslateTransform { IsFrozen: false } move)
        {
            move = new TranslateTransform();
            line.RenderTransform = move;
        }

        if (slides)
        {
            Animate(move, TranslateTransform.XProperty, target.X);
            Animate(line, FrameworkElement.WidthProperty, target.Width);
        }
        else
        {
            move.BeginAnimation(TranslateTransform.XProperty, null);
            line.BeginAnimation(FrameworkElement.WidthProperty, null);
            move.X = target.X;
            line.Width = target.Width;
        }

        move.Y = target.Y;
    }

    // Runs from wherever the value is now, so a new selection takes over a slide that is still under way.
    private static void Animate(IAnimatable target, DependencyProperty property, double to) =>
        target.BeginAnimation(property, new DoubleAnimation(to, MotionSettings.Transition) { EasingFunction = MotionSettings.Ease });
}
