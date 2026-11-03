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

    private void SynchronizeHomeInertia(Action<Action<long>> requestFrame)
    {
        _homeScroll.AddHandler(ScrollGestureInertiaStartingEvent, (_, e) =>
        {
            _pendingHomeFling = null;
            if (Math.Abs(e.Inertia.Y) <= Math.Abs(e.Inertia.X) || !double.IsFinite(e.Inertia.Y))
                return;
            _pendingHomeFling = new PendingFling(e.Id, e.Inertia.Y, _homeScroll.Offset.Y, Stopwatch.GetTimestamp());
        }, RoutingStrategies.Bubble, true);

        _homeScroll.AddHandler(ScrollGestureEvent, (_, e) =>
        {
            if (_pendingHomeFling is not { } pending || pending.Id != e.Id) return;
            _pendingHomeFling = null;
            var offset = _homeScroll.Offset.Y;
            if (offset == pending.Offset) return;

            // The presenter has applied this first step. Stop its inertia loop and continue at display cadence.
            e.ShouldEndScrollGesture = true;
            e.Handled = true;
            StopHomeInertia();
            var velocity = pending.Velocity * Math.Exp(InertiaDecay * Stopwatch.GetElapsedTime(pending.ReleasedAt).TotalSeconds);
            if (Math.Abs(velocity) < 5) return;

            var fling = new DisplayFling(offset, velocity);
            _homeFling = fling;
            requestFrame(Advance);

            void Advance(long frameTime)
            {
                if (!ReferenceEquals(_homeFling, fling)) return;
                if (!_homeScroll.IsEffectivelyVisible) { StopHomeInertia(); return; }
                if (fling.FirstFrame is null)
                {
                    fling.FirstFrame = frameTime;
                    fling.FirstElapsed = Stopwatch.GetElapsedTime(fling.StartedAt).TotalSeconds;
                }

                var elapsed = fling.FirstElapsed + (frameTime - fling.FirstFrame.Value) / 1_000_000_000d;
                var decay = Math.Exp(InertiaDecay * elapsed);
                var distance = fling.Velocity * (decay - 1) / InertiaDecay;
                var bottom = Math.Max(0, _homeScroll.Extent.Height - _homeScroll.Viewport.Height);
                var position = Math.Clamp(fling.Offset + distance, 0, bottom);
                _homeScroll.SetCurrentValue(ScrollViewer.OffsetProperty, new Vector(_homeScroll.Offset.X, position));
                if (position <= 0 || position >= bottom || Math.Abs(fling.Velocity * decay) < 5)
                    StopHomeInertia();
                else
                    requestFrame(Advance);
            }
        }, RoutingStrategies.Bubble, true);

        _homeScroll.AddHandler(ScrollGestureEndedEvent, (_, e) =>
        {
            if (_pendingHomeFling?.Id == e.Id) _pendingHomeFling = null;
        }, RoutingStrategies.Bubble, true);
        AddHandler(PointerPressedEvent, (_, _) => StopHomeInertia(), RoutingStrategies.Tunnel, true);
        _homeScroll.DetachedFromVisualTree += (_, _) => StopHomeInertia();
    }

    private void StopHomeInertia()
    {
        _pendingHomeFling = null;
        _homeFling = null;
    }

    private sealed record PendingFling(int Id, double Velocity, double Offset, long ReleasedAt);

    private sealed class DisplayFling(double offset, double velocity)
    {
        public double Offset { get; } = offset;
        public double Velocity { get; } = velocity;
        public long StartedAt { get; } = Stopwatch.GetTimestamp();
        public long? FirstFrame;
        public double FirstElapsed;
    }
}
