using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using WaNut.App.Services;

namespace WaNut.App.Behaviors;

/// <summary>
/// Lets a progress bar's filled part (the template part named "Fill") glide to each new value instead of jumping.
/// Usage: &lt;Setter Property="behaviors:SmoothProgress.IsEnabled" Value="True" /&gt;
/// </summary>
public static class SmoothProgress
{
    private const string FillPart = "Fill";

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(SmoothProgress), new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ProgressBar bar || e.NewValue is not true)
        {
            return;
        }

        bar.ValueChanged += (_, args) => Update(bar, glides: args.NewValue > args.OldValue);
        // A new width or a first look is not progress: the fill goes straight to its place.
        bar.SizeChanged += (_, _) => Update(bar, glides: false);
        bar.Loaded += (_, _) => Update(bar, glides: false);
    }

    private static void Update(ProgressBar bar, bool glides)
    {
        if (bar.Template?.FindName(FillPart, bar) is not FrameworkElement fill)
        {
            return;
        }

        var range = bar.Maximum - bar.Minimum;
        var width = range > 0 ? bar.ActualWidth * Math.Clamp((bar.Value - bar.Minimum) / range, 0, 1) : 0;
        if (glides && bar.IsLoaded && MotionSettings.IsOn)
        {
            // Runs from the width shown now, so values that arrive faster than the glide keep it moving.
            fill.BeginAnimation(FrameworkElement.WidthProperty, new DoubleAnimation(width, MotionSettings.Transition) { EasingFunction = MotionSettings.Ease });
        }
        else
        {
            fill.BeginAnimation(FrameworkElement.WidthProperty, null);
            fill.Width = width;
        }
    }
}
