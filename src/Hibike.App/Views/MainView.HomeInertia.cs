using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Hibike.App.Views;

public sealed partial class MainView
{
    private static readonly double InertiaDecay = Math.Log(0.15);
    private PendingFling? _pendingHomeFling;
    private DisplayFling? _homeFling;
    private bool _displayInertiaAttached;

    // Avalonia's stock inertia advances on a timer that is not aligned with the display, so flings
    // judder and the collapsing app bars follow those uneven steps. After the first stock step, a
    // vertical fling on any page list continues at display cadence. (Início used to be the only
    // page with this; Biblioteca, Buscar, Downloads, Fila and collections kept the stock timer.)
    private void SynchronizeHomeInertia(Action<Action<long>> requestFrame)
    {
        if (_displayInertiaAttached) return;
        _displayInertiaAttached = true;
        _main.AddHandler(ScrollGestureInertiaStartingEvent, (_, e) =>
        {
            _pendingHomeFling = null;
            if (Math.Abs(e.Inertia.Y) <= Math.Abs(e.Inertia.X) || !double.IsFinite(e.Inertia.Y)) return;
            if (PageScrollFrom(e.Source) is not { } scroll) return;
            _pendingHomeFling = new PendingFling(scroll, e.Id, e.Inertia.Y, scroll.Offset.Y, Stopwatch.GetTimestamp());
        }, RoutingStrategies.Bubble, true);

        _main.AddHandler(ScrollGestureEvent, (_, e) =>
        {
            if (_pendingHomeFling is not { } pending || pending.Id != e.Id) return;
            _pendingHomeFling = null;
            var scroll = pending.Scroll;
            var offset = scroll.Offset.Y;
            if (offset == pending.Offset) return;

            // The presenter has applied this first step. Stop its inertia loop and continue at display cadence.
            e.ShouldEndScrollGesture = true;
            e.Handled = true;
            StopHomeInertia();
            var velocity = pending.Velocity * Math.Exp(InertiaDecay * Stopwatch.GetElapsedTime(pending.ReleasedAt).TotalSeconds);
            if (Math.Abs(velocity) < 5) return;

            var fling = new DisplayFling(scroll, offset, velocity);
            _homeFling = fling;
            requestFrame(Advance);

            void Advance(long frameTime)
            {
                if (!ReferenceEquals(_homeFling, fling)) return;
                if (!scroll.IsEffectivelyVisible) { StopHomeInertia(); return; }
                if (fling.FirstFrame is null)
                {
                    fling.FirstFrame = frameTime;
                    fling.FirstElapsed = Stopwatch.GetElapsedTime(fling.StartedAt).TotalSeconds;
                }

                var elapsed = fling.FirstElapsed + (frameTime - fling.FirstFrame.Value) / 1_000_000_000d;
                var decay = Math.Exp(InertiaDecay * elapsed);
                var distance = fling.Velocity * (decay - 1) / InertiaDecay;
                var bottom = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
                var position = Math.Clamp(fling.Offset + distance, 0, bottom);
                var previous = scroll.Offset.Y;
                scroll.SetCurrentValue(ScrollViewer.OffsetProperty, new Vector(scroll.Offset.X, position));
                if (_androidLayout && position != previous) FollowTopBarScroll(scroll, position - previous);
                if (position <= 0 || position >= bottom || Math.Abs(fling.Velocity * decay) < 5)
                    StopHomeInertia();
                else
                    requestFrame(Advance);
            }
        }, RoutingStrategies.Bubble, true);

        _main.AddHandler(ScrollGestureEndedEvent, (_, e) =>
        {
            if (_pendingHomeFling?.Id == e.Id) _pendingHomeFling = null;
        }, RoutingStrategies.Bubble, true);
        AddHandler(PointerPressedEvent, (_, _) => StopHomeInertia(), RoutingStrategies.Tunnel, true);
        _main.DetachedFromVisualTree += (_, _) => StopHomeInertia();
    }

    private void StopHomeInertia()
    {
        _pendingHomeFling = null;
        _homeFling = null;
    }

    private sealed record PendingFling(ScrollViewer Scroll, int Id, double Velocity, double Offset, long ReleasedAt);

    private sealed class DisplayFling(ScrollViewer scroll, double offset, double velocity)
    {
        public ScrollViewer Scroll { get; } = scroll;
        public double Offset { get; } = offset;
        public double Velocity { get; } = velocity;
        public long StartedAt { get; } = Stopwatch.GetTimestamp();
        public long? FirstFrame;
        public double FirstElapsed;
    }
}
