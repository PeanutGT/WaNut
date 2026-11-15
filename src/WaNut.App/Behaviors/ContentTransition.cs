using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WaNut.App.Services;

namespace WaNut.App.Behaviors;

/// <summary>
/// Fades an element in with a short rise each time the bound value changes, e.g. the selected tab or server
/// (docs/design.md, section 6). The old content is gone at once, so a quick second switch simply restarts it.
/// Usage: &lt;ContentPresenter behaviors:ContentTransition.Trigger="{Binding SelectedServer}" /&gt;
/// </summary>
public static class ContentTransition
{
    // How far below its place the new content starts.
    private const double Rise = 6;

    public static readonly DependencyProperty TriggerProperty = DependencyProperty.RegisterAttached(
        "Trigger", typeof(object), typeof(ContentTransition), new PropertyMetadata(null, OnTriggerChanged));

    public static object? GetTrigger(DependencyObject element) => element.GetValue(TriggerProperty);

    public static void SetTrigger(DependencyObject element, object? value) => element.SetValue(TriggerProperty, value);

    private static void OnTriggerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // Not while the window is still being built: the first look should not move.
        if (d is not FrameworkElement element || !element.IsLoaded || !MotionSettings.IsOn)
        {
            return;
        }

        var duration = MotionSettings.Transition;
        var ease = MotionSettings.Ease;

        // Stop, not hold: afterwards the element is back on its own opacity and position.
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });

        if (element.RenderTransform is not TranslateTransform { IsFrozen: false } move)
        {
            if (element.RenderTransform != Transform.Identity)
            {
                return;
            }

            move = new TranslateTransform();
            element.RenderTransform = move;
        }

        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(Rise, 0, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
    }
}
