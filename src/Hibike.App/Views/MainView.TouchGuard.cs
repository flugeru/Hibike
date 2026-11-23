using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Hibike.App.Views;

// Touch lists previously turned scroll gestures into taps: a finger that stopped a fling, or that
// dragged in a direction the list under it cannot scroll (sideways over a vertical list, along a
// carousel edge), still released over a row or cover and clicked it, so moving through lists
// often started playback. Like Android's own lists, a touch
// that begins while content is moving only stops the motion, and a touch that travels beyond the
// tap slop is never a click.
public sealed partial class MainView
{
    // Close to Android's 8 dp touch slop.
    internal const double TapSlop = 10;
    // A press this soon after a scroll offset changed lands on moving content.
    private static readonly TimeSpan ScrollMotionWindow = TimeSpan.FromMilliseconds(160);
    private long _lastScrollMotion = long.MinValue;
    private IPointer? _tapPointer;
    private Point _tapStart;
    private bool _tapCancelled;

    private void InitializeTouchTapGuard()
    {
        AddHandler(ScrollViewer.ScrollChangedEvent, (_, e) =>
        {
            if (e.OffsetDelta != default) _lastScrollMotion = Stopwatch.GetTimestamp();
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.Pointer.Type != PointerType.Touch) return;
            _tapPointer = e.Pointer;
            _tapStart = e.GetPosition(this);
            _tapCancelled = IsContentMoving();
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerMovedEvent, (_, e) =>
        {
            if (!ReferenceEquals(_tapPointer, e.Pointer) || _tapCancelled) return;
            var travel = e.GetPosition(this) - _tapStart;
            if (travel.X * travel.X + travel.Y * travel.Y > TapSlop * TapSlop) _tapCancelled = true;
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, (_, e) =>
        {
            if (!ReferenceEquals(_tapPointer, e.Pointer)) return;
            var travel = e.GetPosition(this) - _tapStart;
            var cancelled = _tapCancelled || travel.X * travel.X + travel.Y * travel.Y > TapSlop * TapSlop;
            _tapPointer = null;
            _tapCancelled = false;
            // Releasing the button's capture clears its pressed state, so it never raises Click.
            if (cancelled && e.Pointer.Captured is Visual captured && IsInsideButton(captured))
                e.Pointer.Capture(null);
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private bool IsContentMoving() => _lastScrollMotion != long.MinValue
        && Stopwatch.GetElapsedTime(_lastScrollMotion) < ScrollMotionWindow;

    private static bool IsInsideButton(Visual visual) =>
        visual is Button || visual.FindAncestorOfType<Button>() is not null;
}
