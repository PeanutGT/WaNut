using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WaNut.App.Services;

namespace WaNut.App.Behaviors;

/// <summary>
/// Opens an element from zero height when it appears, so what is below it moves down smoothly instead of jumping
/// (banners; docs/design.md, section 6). Usage: &lt;Setter Property="behaviors:Reveal.IsEnabled" Value="True" /&gt;
/// </summary>
public static class Reveal
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(Reveal), new PropertyMetadata(false, OnIsEnabledChanged));

    // False when the element went out of sight with its tab or window: coming back with them is not an appearance.
    private static readonly DependencyProperty HidItselfProperty = DependencyProperty.RegisterAttached(
        "HidItself", typeof(bool), typeof(Reveal), new PropertyMetadata(true));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element && e.NewValue is true)
        {
            element.IsVisibleChanged += OnIsVisibleChanged;
        }
    }

    private static void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        var element = (FrameworkElement)sender;
        var parent = VisualTreeHelper.GetParent(element) as FrameworkElement;
        if (e.NewValue is false)
        {
            element.SetValue(HidItselfProperty, parent is { IsVisible: true });
            return;
        }

        if (!element.IsLoaded || !(bool)element.GetValue(HidItselfProperty) || !MotionSettings.IsOn)
        {
            return;
        }

        // Its height is not known yet: it was collapsed until now.
        element.Measure(new Size(parent?.ActualWidth ?? double.PositiveInfinity, double.PositiveInfinity));
        var margin = element.Margin;
        var height = element.DesiredSize.Height - margin.Top - margin.Bottom;
        if (height <= 0)
        {
            return;
        }

        // Stop, not hold: afterwards the element is back on its own size, so text that wraps later still fits.
        var duration = MotionSettings.Transition;
        var ease = MotionSettings.Ease;
        element.BeginAnimation(FrameworkElement.MaxHeightProperty, new DoubleAnimation(0, height, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        element.BeginAnimation(FrameworkElement.MarginProperty, new ThicknessAnimation(new Thickness(margin.Left, 0, margin.Right, 0), margin, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
    }
}
