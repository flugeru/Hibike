using Avalonia;
using Avalonia.Controls;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Avalonia.Rendering.Composition;

namespace Hibike.App.Views;

internal static class UiMotion
{
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(180);

    public static void Enter(Control control, double offset)
    {
        control.Transitions = null;
        control.Opacity = 0;
        control.RenderTransform = Translation(offset);
        Configure(control);
        Dispatcher.UIThread.Post(() =>
        {
            if (!control.IsHitTestVisible) return;
            control.Opacity = 1;
            control.RenderTransform = Translation(0);
        }, DispatcherPriority.Render);
    }

    public static Task ExitAsync(Control control, double offset)
    {
        Configure(control);
        control.Opacity = 0;
        control.RenderTransform = Translation(offset);
        return Task.Delay(Duration);
    }

    private static void Configure(Control control) => control.Transitions = new Transitions
    {
        new DoubleTransition { Property = Visual.OpacityProperty, Duration = Duration, Easing = new CubicEaseOut() },
        new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = Duration, Easing = new CubicEaseOut() }
    };

    private static TransformOperations Translation(double y)
    {
        var transform = TransformOperations.CreateBuilder(1);
        transform.AppendTranslate(0, y);
        return transform.Build();
    }

    public static Action<int> HorizontalScrollSteps(ScrollViewer scroll)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        double start = 0, target = 0;
        long started = 0;
        var ease = new CubicEaseOut();
        timer.Tick += (_, _) =>
        {
            var progress = Math.Min(1, System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds / Duration.TotalMilliseconds);
            var max = Math.Max(0, scroll.Extent.Width - scroll.Viewport.Width);
            scroll.Offset = new Vector(Math.Clamp(start + (target - start) * ease.Ease(progress), 0, max), scroll.Offset.Y);
            if (progress == 1) timer.Stop();
        };
        scroll.AddHandler(Avalonia.Input.InputElement.PointerWheelChangedEvent, (_, _) => timer.Stop(),
            Avalonia.Interactivity.RoutingStrategies.Tunnel);
        scroll.AddHandler(Avalonia.Input.InputElement.PointerPressedEvent, (_, _) => timer.Stop(),
            Avalonia.Interactivity.RoutingStrategies.Tunnel);
        scroll.DetachedFromVisualTree += (_, _) => timer.Stop();
        return direction =>
        {
            start = scroll.Offset.X;
            var max = Math.Max(0, scroll.Extent.Width - scroll.Viewport.Width);
            target = Math.Clamp((timer.IsEnabled ? target : start) + direction * Math.Max(180, scroll.Viewport.Width * .8), 0, max);
            if (target == start) { timer.Stop(); return; }
            started = System.Diagnostics.Stopwatch.GetTimestamp();
            timer.Start();
        };
    }

    public static void Reflow(Control control)
    {
        control.AttachedToVisualTree += (_, _) =>
        {
            var visual = ElementComposition.GetElementVisual(control);
            if (visual is null) return;
            var animation = visual.Compositor.CreateVector3KeyFrameAnimation();
            animation.Duration = TimeSpan.FromMilliseconds(220);
            animation.Target = "Offset";
            animation.InsertExpressionKeyFrame(1f, "this.FinalValue");
            var animations = visual.Compositor.CreateImplicitAnimationCollection();
            animations["Offset"] = animation;
            visual.ImplicitAnimations = animations;
        };
    }
}
