using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using WaNut.App.Services;

namespace WaNut.App.Behaviors;

/// <summary>
/// Shows a list whose rows open from zero height when they are added and close to zero height when they are removed,
/// so the rows around them move smoothly instead of jumping (docs/design.md, section 6). Use it in place of
/// ItemsSource: &lt;ListBox behaviors:ListMotion.ItemsSource="{Binding Items}" /&gt;. The view model should change
/// the list only where something changed (WaNut.Core.Collections.ListSync); a Reset redraws without motion.
/// </summary>
/// <remarks>
/// The list shows its own copy of the items, so a removed row can stay while it closes. It is out of the view
/// model's list already, so it ignores the mouse until it is gone. Rows only move while the list is on screen and
/// animations are on, and only when a few rows change at once: filling a whole list is not a change worth showing.
/// </remarks>
public static class ListMotion
{
    // More rows than this changing at once, e.g. a first load, appear and go without motion.
    private const int MaxAnimatedRows = 6;

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.RegisterAttached(
        "ItemsSource", typeof(IEnumerable), typeof(ListMotion), new PropertyMetadata(null, OnItemsSourceChanged));

    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(State), typeof(ListMotion), new PropertyMetadata(null));

    public static IEnumerable? GetItemsSource(DependencyObject element) => (IEnumerable?)element.GetValue(ItemsSourceProperty);

    public static void SetItemsSource(DependencyObject element, IEnumerable? value) => element.SetValue(ItemsSourceProperty, value);

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ItemsControl list)
        {
            return;
        }

        if (list.GetValue(StateProperty) is not State state)
        {
            state = new State(list);
            list.SetValue(StateProperty, state);
            list.ItemsSource = state.Shown;
        }

        state.SetSource(e.NewValue as IEnumerable);
    }

    private sealed class State
    {
        private readonly ItemsControl _list;
        private readonly List<object> _leaving = [];
        private readonly List<object> _entering = [];
        private IEnumerable? _source;
        private bool _listening;

        public State(ItemsControl list)
        {
            _list = list;

            // Listen only while on screen, so the view model's list does not keep a closed view alive. Coming back
            // with a tab redraws without motion: that is not a change.
            list.Loaded += (_, _) => Listen(true);
            list.Unloaded += (_, _) => Listen(false);
        }

        public ObservableCollection<object> Shown { get; } = [];

        public void SetSource(IEnumerable? source)
        {
            var listening = _listening;
            Listen(false);
            _source = source;
            if (listening || _list.IsLoaded)
            {
                Listen(true);
            }
            else
            {
                Redraw();
            }
        }

        private void Listen(bool on)
        {
            if (on == _listening)
            {
                return;
            }

            _listening = on;
            if (_source is INotifyCollectionChanged incc)
            {
                if (on)
                {
                    incc.CollectionChanged += OnSourceChanged;
                }
                else
                {
                    incc.CollectionChanged -= OnSourceChanged;
                }
            }

            if (on)
            {
                Redraw();
            }
        }

        private bool CanAnimate(int rows) => rows <= MaxAnimatedRows && MotionSettings.IsOn && _list.IsLoaded && _list.IsVisible;

        private void OnSourceChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add when e.NewItems is { } added:
                    var animate = CanAnimate(added.Count);
                    for (var i = 0; i < added.Count; i++)
                    {
                        var item = added[i]!;

                        // Back before it finished closing: take the closing copy away first.
                        if (_leaving.Remove(item))
                        {
                            if (_list.ItemContainerGenerator.ContainerFromItem(item) is FrameworkElement closing)
                            {
                                StopMotion(closing);
                            }

                            Shown.Remove(item);
                        }

                        Shown.Insert(ShownIndex(e.NewStartingIndex + i), item);
                        if (animate)
                        {
                            Enter(item);
                        }
                    }

                    break;
                case NotifyCollectionChangedAction.Remove when e.OldItems is { } removed:
                    var animateOut = CanAnimate(removed.Count);
                    for (var i = 0; i < removed.Count; i++)
                    {
                        var index = ShownIndex(e.OldStartingIndex);
                        var item = Shown[index];
                        if (animateOut && _list.ItemContainerGenerator.ContainerFromItem(item) is FrameworkElement row)
                        {
                            Leave(item, row);
                        }
                        else
                        {
                            Shown.RemoveAt(index);
                        }
                    }

                    break;
                case NotifyCollectionChangedAction.Replace when e.NewItems is { Count: 1 } && e.OldItems is { Count: 1 }:
                    Shown[ShownIndex(e.NewStartingIndex)] = e.NewItems[0]!;
                    break;
                case NotifyCollectionChangedAction.Move when e.OldItems is { Count: 1 }:
                    var from = ShownIndex(e.OldStartingIndex);
                    var moved = Shown[from];
                    Shown.RemoveAt(from);
                    Shown.Insert(ShownIndex(e.NewStartingIndex), moved);
                    break;
                default:
                    Redraw();
                    break;
            }
        }

        /// <summary>Where the view model's index-th item is in <see cref="Shown"/>, which also holds closing rows.</summary>
        private int ShownIndex(int sourceIndex)
        {
            var count = 0;
            for (var i = 0; i < Shown.Count; i++)
            {
                if (_leaving.Contains(Shown[i]))
                {
                    continue;
                }

                if (count == sourceIndex)
                {
                    return i;
                }

                count++;
            }

            return Shown.Count;
        }

        private void Redraw()
        {
            foreach (var item in _leaving)
            {
                if (_list.ItemContainerGenerator.ContainerFromItem(item) is FrameworkElement row)
                {
                    StopMotion(row);
                }
            }

            _leaving.Clear();
            _entering.Clear();
            Shown.Clear();
            foreach (var item in _source ?? Array.Empty<object>())
            {
                Shown.Add(item);
            }
        }

        // The row's container is made in the next layout pass; open it right after that, before it is drawn.
        private void Enter(object item)
        {
            if (_entering.Count == 0)
            {
                _list.LayoutUpdated += OnLayoutUpdated;
            }

            _entering.Add(item);
        }

        private void OnLayoutUpdated(object? sender, EventArgs e)
        {
            _list.LayoutUpdated -= OnLayoutUpdated;
            var duration = MotionSettings.Transition;
            var ease = MotionSettings.Ease;
            foreach (var item in _entering)
            {
                if (_list.ItemContainerGenerator.ContainerFromItem(item) is not FrameworkElement row || row.ActualHeight <= 0)
                {
                    continue;
                }

                // Stop, not hold: afterwards the row is back on its own size, so text that wraps later still fits.
                row.BeginAnimation(FrameworkElement.MaxHeightProperty, new DoubleAnimation(0, row.ActualHeight, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
                row.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
            }

            _entering.Clear();
        }

        private void Leave(object item, FrameworkElement row)
        {
            _leaving.Add(item);
            _entering.Remove(item);
            row.IsHitTestVisible = false;
            var duration = MotionSettings.Transition;
            var ease = MotionSettings.Ease;
            var close = new DoubleAnimation(row.ActualHeight, 0, duration) { EasingFunction = ease };
            close.Completed += (_, _) =>
            {
                // A Reset may have taken the row already.
                if (_leaving.Remove(item))
                {
                    StopMotion(row);
                    Shown.Remove(item);
                }
            };
            row.BeginAnimation(FrameworkElement.MaxHeightProperty, close);
            row.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, duration) { EasingFunction = ease });
        }

        // Containers are reused for other items, so a closed row must not stay closed.
        private static void StopMotion(FrameworkElement row)
        {
            row.BeginAnimation(FrameworkElement.MaxHeightProperty, null);
            row.BeginAnimation(UIElement.OpacityProperty, null);
            row.IsHitTestVisible = true;
        }
    }
}
