using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.VisualTree;
using Hibike.App.Views;

internal static partial class Program
{
    private static async Task CheckHomeInertiaAsync(MainView view)
    {
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        var scroll = (ScrollViewer)typeof(MainView).GetField("_homeScroll", hidden)!.GetValue(view)!;
        typeof(MainView).GetField("_page", hidden)!.SetValue(view, "home");
        typeof(MainView).GetMethod("Refresh", hidden)!.Invoke(view, null);
        scroll.Content = new Border { Height = 5000 };
        await Task.Delay(100);
        var presenter = scroll.GetVisualDescendants().OfType<ScrollContentPresenter>().First();
        Action<long>? next = null;
        Action<Action<long>> requestFrame = callback =>
        {
            Check(next is null, "Home inertia queued more than one frame");
            next = callback;
        };
        typeof(MainView).GetMethod("SynchronizeHomeInertia", hidden)!.Invoke(view, [requestFrame]);

        void Start(int id, Vector velocity) => presenter.RaiseEvent(
            (ScrollGestureInertiaStartingEventArgs)Activator.CreateInstance(typeof(ScrollGestureInertiaStartingEventArgs),
                hidden, null, [id, velocity], null)!);
        ScrollGestureEventArgs Step(int id, Vector delta)
        {
            var step = new ScrollGestureEventArgs(id, delta);
            presenter.RaiseEvent(step);
            return step;
        }
        void Frame(long time)
        {
            var callback = next;
            next = null;
            Check(callback is not null, "Home inertia did not schedule a frame");
            callback!(time);
        }

        Start(100, new Vector(1200, 30));
        Step(100, new Vector(12, 0));
        Check(next is null, "Horizontal inertia was replaced");
        presenter.RaiseEvent(new ScrollGestureEndedEventArgs(100));

        Start(101, new Vector(0, 1400));
        var stockStep = Step(101, new Vector(0, 12));
        Check(stockStep.ShouldEndScrollGesture && scroll.Offset.Y == 12, "Home inertia did not take over after the stock step");
        presenter.RaiseEvent(new ScrollGestureEndedEventArgs(101));
        Frame(1_000_000_000);
        var firstOffset = scroll.Offset.Y;
        Frame(1_008_333_333);
        Check(scroll.Offset.Y > firstOffset, "Home inertia did not advance with display time");
        typeof(MainView).GetMethod("StopHomeInertia", hidden)!.Invoke(view, null);
        var stoppedOffset = scroll.Offset.Y;
        Frame(1_016_666_666);
        Check(next is null && scroll.Offset.Y == stoppedOffset, "Cancelled inertia changed the scroll position");

        var bottom = scroll.Extent.Height - scroll.Viewport.Height;
        scroll.Offset = new Vector(0, bottom - 10);
        Start(102, new Vector(0, 1400));
        Step(102, new Vector(0, 2));
        Frame(2_000_000_000);
        if (next is not null) Frame(2_016_666_666);
        Check(next is null && scroll.Offset.Y == bottom, "Home inertia did not stop at the scroll boundary");
    }
}
