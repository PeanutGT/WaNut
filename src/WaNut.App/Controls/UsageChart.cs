using System.Windows;
using System.Windows.Media;

namespace WaNut.App.Controls;

/// <summary>
/// A filled line of values from 0 to 1, newest on the right, with faint lines at a quarter, half and three quarters.
/// The chart always spans <see cref="Slots"/> values, so it fills up from the right as samples arrive.
/// </summary>
public sealed class UsageChart : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<double>), typeof(UsageChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SlotsProperty = DependencyProperty.Register(
        nameof(Slots), typeof(int), typeof(UsageChart),
        new FrameworkPropertyMetadata(300, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(UsageChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GridBrushProperty = DependencyProperty.Register(
        nameof(GridBrush), typeof(Brush), typeof(UsageChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Each from 0 to 1; a new list instance redraws the chart.</summary>
    public IReadOnlyList<double>? Values
    {
        get => (IReadOnlyList<double>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    /// <summary>How many values fit across the chart.</summary>
    public int Slots
    {
        get => (int)GetValue(SlotsProperty);
        set => SetValue(SlotsProperty, value);
    }

    public Brush? Stroke
    {
        get => (Brush?)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public Brush? GridBrush
    {
        get => (Brush?)GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        // Transparent background so the whole area takes the tooltip and hit tests.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));
        if (GridBrush is { } grid)
        {
            var pen = new Pen(grid, 1);
            foreach (var share in (double[])[0.25, 0.5, 0.75])
            {
                var y = Math.Round(height * share) + 0.5;
                dc.DrawLine(pen, new Point(0, y), new Point(width, y));
            }
        }

        if (Values is not { Count: > 1 } values || Stroke is not { } stroke)
        {
            return;
        }

        var shown = values.Count > Slots ? values.Skip(values.Count - Slots).ToList() : values;
        var step = width / Math.Max(1, Slots - 1);
        var left = width - ((shown.Count - 1) * step);
        var points = shown.Select((v, i) => new Point(left + (i * step), height - (Math.Clamp(v, 0, 1) * (height - 2)) - 1)).ToList();

        var line = new StreamGeometry();
        var area = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(points[0], false, false);
            ctx.PolyLineTo(points.Skip(1).ToList(), true, true);
        }

        using (var ctx = area.Open())
        {
            ctx.BeginFigure(new Point(points[0].X, height), true, true);
            ctx.PolyLineTo([.. points, new Point(points[^1].X, height)], false, true);
        }

        line.Freeze();
        area.Freeze();
        var fill = stroke.CloneCurrentValue();
        fill.Opacity = 0.18;
        dc.DrawGeometry(fill, null, area);
        dc.DrawGeometry(null, new Pen(stroke, 2) { LineJoin = PenLineJoin.Round }, line);
    }
}
