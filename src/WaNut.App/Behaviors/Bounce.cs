using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WaNut.App.Services;

namespace WaNut.App.Behaviors;

/// <summary>
/// The one playful motion: an element pops in slightly past its size and settles. Only for the peanut and the
/// success moments listed in docs/design.md, section 6. Usage: behaviors:Bounce.OnAppear="True" for something that
/// shows up on success, or behaviors:Bounce.When="{Binding IsOnline}" for something already on screen.
/// </summary>
public static class Bounce
{
    // The size it starts from, as a share of its own.
    private const double From = 0.6;

    public static readonly DependencyProperty OnAppearProperty = DependencyProperty.RegisterAttached(
        "OnAppear", typeof(bool), typeof(Bounce), new PropertyMetadata(false, OnOnAppearChanged));

    /// <summary>Bounces each time the bound value turns true.</summary>
    public static readonly DependencyProperty WhenProperty = DependencyProperty.RegisterAttached(
        "When", typeof(bool), typeof(Bounce), new PropertyMetadata(false, OnWhenChanged));

    public static bool GetOnAppear(DependencyObject element) => (bool)element.GetValue(OnAppearProperty);

    public static void SetOnAppear(DependencyObject element, bool value) => element.SetValue(OnAppearProperty, value);

    public static bool GetWhen(DependencyObject element) => (bool)element.GetValue(WhenProperty);

    public static void SetWhen(DependencyObject element, bool value) => element.SetValue(WhenProperty, value);

    private static void OnOnAppearChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element && e.NewValue is true)
        {
            element.IsVisibleChanged += (_, args) =>
            {
                if (args.NewValue is true)
                {
                    Play(element);
                }
            };
        }
    }

    private static void OnWhenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element && e.NewValue is true)
        {
            Play(element);
        }
    }

    private static void Play(FrameworkElement element)
    {
        // Not for what is already there when a window or tab opens: only a change is a moment.
        if (!element.IsLoaded || !element.IsVisible || !MotionSettings.IsOn)
        {
            return;
        }

        if (element.RenderTransform is not ScaleTransform { IsFrozen: false } scale)
        {
            if (element.RenderTransform != Transform.Identity)
            {
                return;
            }

            scale = new ScaleTransform();
            element.RenderTransform = scale;
            element.RenderTransformOrigin = new Point(0.5, 0.5);
        }

        var pop = new DoubleAnimation(From, 1, MotionSettings.Bounce) { EasingFunction = MotionSettings.BounceEase, FillBehavior = FillBehavior.Stop };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
    }
}
