using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;

namespace Hibike.App.Views;

internal sealed class CollapsibleTopBar : Decorator
{
    private static readonly StyledProperty<double> RevealProperty =
        AvaloniaProperty.Register<CollapsibleTopBar, double>(nameof(Reveal), 1);

    public double ExpandedHeight { get; private set; }

    static CollapsibleTopBar()
    {
        AffectsMeasure<CollapsibleTopBar>(RevealProperty);
        RevealProperty.Changed.AddClassHandler<CollapsibleTopBar>((bar, _) =>
        {
            var visible = bar.Reveal > 0;
            bar.IsHitTestVisible = visible;
            bar.IsEnabled = visible;
        });
    }

    public CollapsibleTopBar() => ClipToBounds = true;

    public double Reveal
    {
        get => GetValue(RevealProperty);
        set => SetValue(RevealProperty, value);
    }

    public void SetReveal(double fraction, TimeSpan duration = default)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        if (duration == TimeSpan.Zero && Transitions is null && Math.Abs(Reveal - fraction) < .001) return;
        if (duration == TimeSpan.Zero || UiMotion.Reduced) Transitions = null;
        else Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = RevealProperty,
                Duration = duration,
                Easing = new CubicEaseOut()
            }
        };
        Reveal = fraction;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Child?.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        var desired = Child?.DesiredSize ?? default;
        ExpandedHeight = desired.Height;
        return new Size(desired.Width, ExpandedHeight * Reveal);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(new Rect(0, finalSize.Height - ExpandedHeight, finalSize.Width, ExpandedHeight));
        return finalSize;
    }
}
