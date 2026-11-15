using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WaNut.App.Services;

namespace WaNut.App.Controls;

/// <summary>
/// Shows a small question as a panel inside a window instead of a separate dialog window (docs/design.md,
/// section 7): a dim layer over the window, the panel in the middle, Esc or a click on the dim layer to back out,
/// and keyboard focus kept inside. <see cref="Show"/> waits for the answer like ShowDialog does, so callers
/// stay as they were. A button answers with &lt;Button controls:InlinePanel.Answer="True" /&gt;.
/// </summary>
public static class InlinePanel
{
    // The part of the AppWindow template that panels are put in.
    private const string LayerPart = "PanelLayer";

    // How far below its place the panel starts.
    private const double Rise = 12;

    /// <summary>On a button: clicking it closes the panel with this answer.</summary>
    public static readonly DependencyProperty AnswerProperty = DependencyProperty.RegisterAttached(
        "Answer", typeof(bool?), typeof(InlinePanel), new PropertyMetadata(null, OnAnswerChanged));

    /// <summary>On the element that has keyboard focus when the panel opens; without one, the first control.</summary>
    public static readonly DependencyProperty TakesFocusProperty = DependencyProperty.RegisterAttached(
        "TakesFocus", typeof(bool), typeof(InlinePanel), new PropertyMetadata(false));

    /// <summary>On the button that Enter presses when focus is not on a button.</summary>
    public static readonly DependencyProperty IsDefaultProperty = DependencyProperty.RegisterAttached(
        "IsDefault", typeof(bool), typeof(InlinePanel), new PropertyMetadata(false));

    // On the layer of an open panel: how to close it.
    private static readonly DependencyProperty CloseProperty = DependencyProperty.RegisterAttached(
        "Close", typeof(Action<bool>), typeof(InlinePanel), new PropertyMetadata(null));

    public static bool? GetAnswer(DependencyObject element) => (bool?)element.GetValue(AnswerProperty);

    public static void SetAnswer(DependencyObject element, bool? value) => element.SetValue(AnswerProperty, value);

    public static bool GetTakesFocus(DependencyObject element) => (bool)element.GetValue(TakesFocusProperty);

    public static void SetTakesFocus(DependencyObject element, bool value) => element.SetValue(TakesFocusProperty, value);

    public static bool GetIsDefault(DependencyObject element) => (bool)element.GetValue(IsDefaultProperty);

    public static void SetIsDefault(DependencyObject element, bool value) => element.SetValue(IsDefaultProperty, value);

    /// <summary>True when the window is one of ours and on screen, so a panel can open in it.</summary>
    public static bool CanShowIn(Window window) => window.IsVisible && Layer(window) is not null;

    /// <summary>Opens <paramref name="body"/> as a panel in the window and waits. True only for an Answer="True" button.</summary>
    public static bool Show(Window window, FrameworkElement body)
    {
        var layer = Layer(window) ?? throw new InvalidOperationException("The window has no panel layer; check CanShowIn first.");
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();

        var dim = new Border();
        dim.SetResourceReference(Border.BackgroundProperty, "GuideDimBrush");
        var card = new Border { Child = body, Style = (Style)window.FindResource("PanelCard"), RenderTransform = new TranslateTransform() };
        var overlay = new Grid { Children = { dim, card } };
        KeyboardNavigation.SetTabNavigation(overlay, KeyboardNavigationMode.Cycle);
        KeyboardNavigation.SetControlTabNavigation(overlay, KeyboardNavigationMode.Cycle);
        KeyboardNavigation.SetDirectionalNavigation(overlay, KeyboardNavigationMode.Cycle);

        var answer = false;
        var frame = new DispatcherFrame();
        void Close(bool value)
        {
            answer = value;
            frame.Continue = false;
        }

        overlay.SetValue(CloseProperty, (Action<bool>)Close);
        dim.MouseLeftButtonDown += (_, _) => Close(false);
        overlay.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close(false);
            }
            else if (e.Key == Key.Enter && Find<ButtonBase>(body, GetIsDefault) is { IsEnabled: true } button)
            {
                // A focused button has already taken Enter itself; this is Enter from a check box or a text box.
                e.Handled = true;
                button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            }
        };

        // Like a dialog over it: the window cannot be closed while a question is open.
        void KeepOpen(object? sender, CancelEventArgs e) => e.Cancel = true;
        window.Closing += KeepOpen;

        var focusBefore = Keyboard.FocusedElement;
        layer.Children.Add(overlay);
        overlay.UpdateLayout();
        if (!(Find<UIElement>(body, GetTakesFocus)?.Focus() ?? false))
        {
            body.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        }

        if (MotionSettings.IsOn)
        {
            var duration = MotionSettings.Transition;
            var ease = MotionSettings.Ease;
            overlay.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
            card.RenderTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(Rise, 0, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        }

        Dispatcher.PushFrame(frame);

        window.Closing -= KeepOpen;
        overlay.IsHitTestVisible = false;
        focusBefore?.Focus();
        if (MotionSettings.IsOn)
        {
            var fade = new DoubleAnimation(0, MotionSettings.Transition) { EasingFunction = MotionSettings.Ease };
            fade.Completed += (_, _) => layer.Children.Remove(overlay);
            overlay.BeginAnimation(UIElement.OpacityProperty, fade);
        }
        else
        {
            layer.Children.Remove(overlay);
        }

        return answer;
    }

    /// <summary>Closes the panel that <paramref name="inside"/> is in, for content that has more to record than yes or no.</summary>
    public static void Close(DependencyObject inside, bool answer)
    {
        for (DependencyObject? at = inside; at is not null; at = VisualTreeHelper.GetParent(at))
        {
            if (at.GetValue(CloseProperty) is Action<bool> close)
            {
                close(answer);
                return;
            }
        }
    }

    private static Panel? Layer(Window window) => window.Template?.FindName(LayerPart, window) as Panel;

    private static void OnAnswerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ButtonBase button && e.OldValue is null && e.NewValue is not null)
        {
            button.Click += (_, _) => Close(button, GetAnswer(button) == true);
        }
    }

    private static T? Find<T>(DependencyObject root, Func<T, bool> matches)
        where T : UIElement
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T { IsVisible: true } element && matches(element))
            {
                return element;
            }

            if (Find(child, matches) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
