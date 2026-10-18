using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WaNut.App.ViewModels;
using WaNut.Core.Onboarding;

namespace WaNut.App.Views;

/// <summary>Dims the window, rings the part the current guide stop is about and puts the peanut's bubble beside it.</summary>
public partial class GuideOverlay : UserControl
{
    private const double Gap = 12;
    private const double RingPadding = 6;

    private GuideViewModel? _guide;
    private FrameworkElement? _target;

    public GuideOverlay()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as GuideViewModel);
        SizeChanged += (_, _) => Place();
        Bubble.SizeChanged += (_, _) => Place();
    }

    /// <summary>Set by the main window: selects what a stop needs (such as its tab) and returns the element to ring.</summary>
    public Func<GuideStop, FrameworkElement?>? FindTarget { get; set; }

    private void Attach(GuideViewModel? guide)
    {
        if (_guide is not null)
        {
            _guide.PropertyChanged -= OnGuideChanged;
        }

        _guide = guide;
        if (guide is not null)
        {
            guide.PropertyChanged += OnGuideChanged;
        }
    }

    private void OnGuideChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(GuideViewModel.Current))
        {
            return;
        }

        if (_guide?.Current is not { } stop)
        {
            _target = null;
            return;
        }

        _target = FindTarget?.Invoke(stop);

        // A newly selected tab and the new text are laid out first, so the ring and bubble land on their final sizes.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            Place();
            NextButton.Focus();
        });
    }

    private void Place()
    {
        if (_guide?.IsOpen != true || ActualWidth <= 0)
        {
            return;
        }

        var area = new Size(ActualWidth, ActualHeight);
        var bubble = new Size(Bubble.ActualWidth, Bubble.ActualHeight);
        var whole = new RectangleGeometry(new Rect(area));
        if (_target is not { IsVisible: true, ActualWidth: > 0 } target)
        {
            Dim.Data = whole;
            Ring.Visibility = Visibility.Collapsed;
            Bubble.Margin = new Thickness((area.Width - bubble.Width) / 2, (area.Height - bubble.Height) / 2, 0, 0);
            return;
        }

        var ring = target.TransformToVisual(this).TransformBounds(new Rect(0, 0, target.ActualWidth, target.ActualHeight));
        ring.Inflate(RingPadding, RingPadding);
        Dim.Data = new CombinedGeometry(GeometryCombineMode.Exclude, whole, new RectangleGeometry(ring, 8, 8));
        Ring.Visibility = Visibility.Visible;
        Ring.Margin = new Thickness(ring.Left, ring.Top, 0, 0);
        Ring.Width = ring.Width;
        Ring.Height = ring.Height;
        var spot = BubbleSpot(ring, bubble, area);
        Bubble.Margin = new Thickness(spot.X, spot.Y, 0, 0);
    }

    /// <summary>Below the ring, else right of it, above it, left of it, and as a last resort in the middle.</summary>
    private static Point BubbleSpot(Rect ring, Size bubble, Size area)
    {
        var left = Math.Clamp(ring.Left, Gap, Math.Max(Gap, area.Width - bubble.Width - Gap));
        var top = Math.Clamp(ring.Top, Gap, Math.Max(Gap, area.Height - bubble.Height - Gap));
        if (ring.Bottom + Gap + bubble.Height <= area.Height - Gap)
        {
            return new Point(left, ring.Bottom + Gap);
        }

        if (ring.Right + Gap + bubble.Width <= area.Width - Gap)
        {
            return new Point(ring.Right + Gap, top);
        }

        if (ring.Top - Gap - bubble.Height >= Gap)
        {
            return new Point(left, ring.Top - Gap - bubble.Height);
        }

        if (ring.Left - Gap - bubble.Width >= Gap)
        {
            return new Point(ring.Left - Gap - bubble.Width, top);
        }

        return new Point((area.Width - bubble.Width) / 2, (area.Height - bubble.Height) / 2);
    }
}
