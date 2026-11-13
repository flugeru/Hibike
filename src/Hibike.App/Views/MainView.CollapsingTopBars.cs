using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Threading;

namespace Hibike.App.Views;

public sealed partial class MainView
{
    private const double TopBarGestureThreshold = 8;
    private CollapsibleTopBar[] _activeTopBars = [];
    private ScrollViewer? _topBarScroll;
    private int? _topBarGesture;
    private double _topBarHidden;
    private double _topBarDirection;
    private IPointer? _topBarPointer;
    private Point _topBarPointerStart;
    private Point _topBarPointerLast;
    private bool _revealingAtLeadingEdge;
    private bool _topBarsSettling;
    private readonly DispatcherTimer _topBarIdle = new() { Interval = TimeSpan.FromMilliseconds(110) };

    private void InitializeCollapsingTopBars(Grid content)
    {
        if (!_androidLayout) return;

        MakeTopRowsCollapsible(content, 1);
        _topBarIdle.Tick += (_, _) => SettleTopBars();
        _main.AddHandler(ScrollGestureEvent, (_, e) =>
        {
            if (_revealingAtLeadingEdge || Math.Abs(e.Delta.Y) <= Math.Abs(e.Delta.X)) return;
            if (PageScrollFrom(e.Source) is not { } scroll) return;
            FollowTopBarScroll(scroll, e.Delta.Y, e.Id, shareScroll: e.Handled);
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        _main.AddHandler(PointerWheelChangedEvent, (_, e) =>
        {
            if (Math.Abs(e.Delta.Y) <= Math.Abs(e.Delta.X)) return;
            if (PageScrollFrom(e.Source) is { } scroll)
                FollowTopBarScroll(scroll, -e.Delta.Y * UiTheme.TouchSize);
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        _main.AddHandler(ScrollGestureEndedEvent, (_, e) =>
        {
            if (_topBarGesture == e.Id)
            {
                _topBarGesture = null;
                ScheduleTopBarSettle();
            }
        }, RoutingStrategies.Bubble, handledEventsToo: true);

        // Avalonia can suppress a reverse gesture when the list starts at offset zero.
        _main.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.Pointer.Type != PointerType.Touch || _topBarScroll is not { Offset.Y: <= 1 } scroll
                || !_activeTopBars.Any(bar => bar.Reveal < 1)
                || !ReferenceEquals(PageScrollFrom(e.Source), scroll)) return;
            _topBarPointer = e.Pointer;
            _topBarPointerStart = _topBarPointerLast = e.GetPosition(_main);
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        _main.AddHandler(PointerMovedEvent, (_, e) =>
        {
            if (!ReferenceEquals(_topBarPointer, e.Pointer) || _topBarScroll is not { } scroll) return;
            var position = e.GetPosition(_main);
            var travel = position - _topBarPointerStart;
            if (!_revealingAtLeadingEdge)
            {
                if (travel.Y < -TopBarGestureThreshold || Math.Abs(travel.X) > TopBarGestureThreshold
                    && Math.Abs(travel.X) >= Math.Abs(travel.Y))
                {
                    _topBarPointer = null;
                    return;
                }
                if (travel.Y < TopBarGestureThreshold) return;
                _revealingAtLeadingEdge = true;
            }
            FollowTopBarScroll(scroll, _topBarPointerLast.Y - position.Y);
            _topBarPointerLast = position;
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        _main.AddHandler(PointerReleasedEvent, (_, e) => FinishLeadingEdgeGesture(e.Pointer),
            RoutingStrategies.Tunnel, handledEventsToo: true);
        _main.AddHandler(PointerCaptureLostEvent, (_, e) => FinishLeadingEdgeGesture(e.Pointer),
            RoutingStrategies.Direct, handledEventsToo: true);
    }

    private void FinishLeadingEdgeGesture(IPointer pointer)
    {
        if (!ReferenceEquals(_topBarPointer, pointer)) return;
        _topBarPointer = null;
        _revealingAtLeadingEdge = false;
        ScheduleTopBarSettle();
    }

    private static ScrollViewer? PageScrollFrom(object? source)
    {
        if (source is not Visual visual) return null;
        var scroll = visual as ScrollViewer ?? visual.FindAncestorOfType<ScrollViewer>();
        return scroll is { IsEffectivelyVisible: true } && scroll.Classes.Contains("contentScroll") ? scroll : null;
    }

    private void FollowTopBarScroll(ScrollViewer scroll, double distance, int? gesture = null, bool shareScroll = false)
    {
        if (_showingPlayer || _topMenuOpen || _overlay is not null || _googleWelcome is not null || _onboarding is not null)
            return;

        if (!ReferenceEquals(_topBarScroll, scroll))
        {
            RevealMobileTopBars();
            _topBarScroll = scroll;
            _activeTopBars = scroll.GetVisualAncestors().OfType<Grid>().Reverse()
                .SelectMany(grid => grid.Children.OfType<CollapsibleTopBar>())
                .Where(bar => bar.IsEffectivelyVisible).ToArray();
        }

        if (gesture is not null) _topBarGesture = gesture;
        if (_activeTopBars.Length == 0 || distance == 0) return;
        if (distance > 0 && scroll.Extent.Height <= scroll.Viewport.Height + 1) return;
        if (distance > 0 && TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox input
            && _activeTopBars.Any(bar => bar.IsVisualAncestorOf(input))) return;

        var height = _activeTopBars.Sum(bar => bar.ExpandedHeight);
        if (_topBarsSettling)
        {
            // Pick up an interrupted transition at its current visual position.
            _topBarHidden = _activeTopBars.Sum(bar => (1 - bar.Reveal) * bar.ExpandedHeight);
            _topBarsSettling = false;
        }
        var previousHidden = _topBarHidden;
        _topBarHidden = Math.Clamp(previousHidden + distance, 0, height);
        _topBarDirection = distance;
        if (shareScroll && scroll.Offset.Y > 1)
        {
            // The presenter already applied this gesture. Give back the part used by
            // the toolbar so the list and toolbar do not both move by the full delta.
            var offset = scroll.Offset;
            scroll.SetCurrentValue(ScrollViewer.OffsetProperty, new Vector(offset.X, offset.Y - (_topBarHidden - previousHidden)));
        }
        ApplyTopBarReveal(_topBarHidden, height, default);
        if (_topBarGesture is null && !_revealingAtLeadingEdge) ScheduleTopBarSettle();
        else _topBarIdle.Stop();
    }

    private void ScheduleTopBarSettle()
    {
        _topBarIdle.Stop();
        if (_activeTopBars.Length > 0) _topBarIdle.Start();
    }

    private void SettleTopBars()
    {
        _topBarIdle.Stop();
        if (_topBarGesture is not null || _revealingAtLeadingEdge) return;
        var height = _activeTopBars.Sum(bar => bar.ExpandedHeight);
        var target = _topBarDirection < 0 || _topBarHidden < height * .35 ? 0 : height;
        var remaining = Math.Abs(target - _topBarHidden);
        if (remaining < .5) return;
        _topBarsSettling = true;
        ApplyTopBarReveal(target, height, TimeSpan.FromMilliseconds(Math.Clamp(remaining * .65, 65, 140)));
    }

    private void ApplyTopBarReveal(double hidden, double height, TimeSpan duration)
    {
        var remaining = Math.Clamp(hidden, 0, height);
        foreach (var bar in _activeTopBars)
        {
            var size = bar.ExpandedHeight;
            bar.SetReveal(size > 0 ? 1 - Math.Min(remaining, size) / size : 1, duration);
            remaining = Math.Max(0, remaining - size);
        }
    }

    private void RevealMobileTopBars()
    {
        if (!_androidLayout) return;
        _topBarIdle.Stop();
        foreach (var bar in _activeTopBars) bar.SetReveal(1);
        _activeTopBars = [];
        _topBarScroll = null;
        _topBarGesture = null;
        _topBarPointer = null;
        _revealingAtLeadingEdge = false;
        _topBarsSettling = false;
        _topBarHidden = _topBarDirection = 0;
    }

    private void MakeTopRowsCollapsible(Grid page, int count)
    {
        if (!_androidLayout) return;
        var heading = new Grid { RowSpacing = page.RowSpacing };
        for (var index = 0; index < count; index++)
        {
            var row = page.RowDefinitions[0];
            page.RowDefinitions.RemoveAt(0);
            heading.RowDefinitions.Add(row);
        }
        page.RowDefinitions.Insert(0, new RowDefinition(GridLength.Auto));

        foreach (var child in page.Children.ToArray())
        {
            var row = Grid.GetRow(child);
            if (row < count)
            {
                page.Children.Remove(child);
                heading.Children.Add(child);
            }
            else Grid.SetRow(child, row - count + 1);
        }

        page.Children.Insert(0, new CollapsibleTopBar { Child = heading });
    }

    private static Control CollectionBody(Grid page) =>
        page.Children.Single(child => Grid.GetRow(child) == page.RowDefinitions.Count - 1);

    private static WrapPanel CollectionActions(Grid page)
    {
        var heading = page.Children.OfType<CollapsibleTopBar>().FirstOrDefault()?.Child as Grid ?? page;
        return heading.Children.OfType<WrapPanel>().Single();
    }
}
