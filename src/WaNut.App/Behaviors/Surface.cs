using System.Windows;
using System.Windows.Media;

namespace WaNut.App.Behaviors;

/// <summary>
/// Brushes a control template needs besides Background and BorderBrush, so one template serves every button style.
/// Usage: &lt;Setter Property="behaviors:Surface.HoverBrush" Value="{DynamicResource HoverBrush}" /&gt;
/// </summary>
public static class Surface
{
    /// <summary>The layer that fades in over the background while the pointer is over the control.</summary>
    public static readonly DependencyProperty HoverBrushProperty = DependencyProperty.RegisterAttached(
        "HoverBrush", typeof(Brush), typeof(Surface), new PropertyMetadata(null));

    /// <summary>The solid 3px bottom edge that marks something as pressable (docs/design.md, section 6).</summary>
    public static readonly DependencyProperty EdgeBrushProperty = DependencyProperty.RegisterAttached(
        "EdgeBrush", typeof(Brush), typeof(Surface), new PropertyMetadata(null));

    public static Brush? GetHoverBrush(DependencyObject element) => (Brush?)element.GetValue(HoverBrushProperty);

    public static void SetHoverBrush(DependencyObject element, Brush? value) => element.SetValue(HoverBrushProperty, value);

    public static Brush? GetEdgeBrush(DependencyObject element) => (Brush?)element.GetValue(EdgeBrushProperty);

    public static void SetEdgeBrush(DependencyObject element, Brush? value) => element.SetValue(EdgeBrushProperty, value);
}
