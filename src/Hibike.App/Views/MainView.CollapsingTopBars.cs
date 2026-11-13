using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Hibike.App.Views;

public sealed partial class MainView
{
    private const double TopBarGestureThreshold = 14;
    private CollapsibleTopBar[] _activeTopBars = [];
    private ScrollViewer? _topBarScroll;
    private double _topBarTravel;
    private bool _topBarsCollapsed;
    private IPointer? _topBarPointer;
    private Point _topBarPointerStart;

    private void InitializeCollapsingTopBars(Grid content)
    {
        if (!_androidLayout) return;

        MakeTopRowsCollapsible(content, 1);
        _main.AddHandler(ScrollGestureEvent, (_, e) =>
        {
            if (Math.Abs(e.Delta.Y) <= Math.Abs(e.Delta.X)) return;
            if (PageScrollFrom(e.Source) is { } scroll) FollowTopBarScroll(scroll, e.Delta.Y);
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        _main.AddHandler(PointerWheelChangedEvent, (_, e) =>
        {
            if (Math.Abs(e.Delta.Y) <= Math.Abs(e.Delta.X)) return;
            if (PageScrollFrom(e.Source) is { } scroll)
                FollowTopBarScroll(scroll, -e.Delta.Y * UiTheme.TouchSize);
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        _main.AddHandler(ScrollGestureEndedEvent, (_, _) => _topBarTravel = 0,
            RoutingStrategies.Bubble, handledEventsToo: true);

        // Avalonia does not start a scroll gesture when the list is already at its leading edge.
        // A short list can reach that edge as the toolbar releases space.
        _main.AddHandler(PointerPressedEvent, (_, e) =>
        {
            _topBarTravel = 0;
            if (!_topBarsCollapsed || e.Pointer.Type != PointerType.Touch || _topBarScroll is not { Offset.Y: <= 1 } scroll
                || !ReferenceEquals(PageScrollFrom(e.Source), scroll)) return;
            _topBarPointer = e.Pointer;
            _topBarPointerStart = e.GetPosition(_main);
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        _main.AddHandler(PointerMovedEvent, (_, e) =>
        {
            if (!ReferenceEquals(_topBarPointer, e.Pointer)) return;
            var delta = e.GetPosition(_main) - _topBarPointerStart;
            if (delta.Y < TopBarGestureThreshold || delta.Y <= Math.Abs(delta.X)) return;
            RevealMobileTopBars(animate: true);
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        _main.AddHandler(PointerReleasedEvent, (_, e) =>
        {
            if (ReferenceEquals(_topBarPointer, e.Pointer)) _topBarPointer = null;
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        _main.AddHandler(PointerCaptureLostEvent, (_, e) =>
        {
            if (ReferenceEquals(_topBarPointer, e.Pointer)) _topBarPointer = null;
        }, RoutingStrategies.Direct, handledEventsToo: true);
    }

    private static ScrollViewer? PageScrollFrom(object? source)
    {
        if (source is not Visual visual) return null;
        var scroll = visual as ScrollViewer ?? visual.FindAncestorOfType<ScrollViewer>();
        return scroll is { IsEffectivelyVisible: true } && scroll.Classes.Contains("contentScroll") ? scroll : null;
    }

    private void FollowTopBarScroll(ScrollViewer scroll, double distance)
    {
        if (_showingPlayer || _topMenuOpen || _overlay is not null || _googleWelcome is not null || _onboarding is not null)
            return;

        if (!ReferenceEquals(_topBarScroll, scroll))
        {
            RevealMobileTopBars();
            _topBarScroll = scroll;
            _activeTopBars = scroll.GetVisualAncestors().OfType<Grid>()
                .SelectMany(grid => grid.Children.OfType<CollapsibleTopBar>())
                .Where(bar => bar.IsEffectivelyVisible).ToArray();
        }

        if (_activeTopBars.Length == 0 || distance > 0 && scroll.Extent.Height <= scroll.Viewport.Height + 1)
            return;
        if (distance > 0 && TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox input
            && _activeTopBars.Any(bar => bar.IsVisualAncestorOf(input))) return;

        if (Math.Sign(distance) != Math.Sign(_topBarTravel)) _topBarTravel = 0;
        _topBarTravel += distance;
        if (Math.Abs(_topBarTravel) < TopBarGestureThreshold && (distance > 0 || scroll.Offset.Y > 1)) return;
        var collapse = distance > 0;
        if (_topBarsCollapsed == collapse) return;
        _topBarsCollapsed = collapse;
        _topBarTravel = 0;
        foreach (var bar in _activeTopBars) bar.SetExpanded(!collapse, animate: true);
    }

    private void RevealMobileTopBars(bool animate = false)
    {
        if (!_androidLayout) return;
        foreach (var bar in _activeTopBars) bar.SetExpanded(true, animate);
        _activeTopBars = [];
        _topBarScroll = null;
        _topBarPointer = null;
        _topBarTravel = 0;
        _topBarsCollapsed = false;
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
