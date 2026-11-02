using Avalonia;
using Avalonia.Controls;
using Avalonia.Rendering.Composition;

namespace Hibike.App.Views;

internal static class UiMotion
{
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
