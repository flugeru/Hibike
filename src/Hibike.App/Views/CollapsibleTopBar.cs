using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;

namespace Hibike.App.Views;

internal sealed class CollapsibleTopBar : Decorator
{
    private static readonly StyledProperty<double> RevealProperty =
        AvaloniaProperty.Register<CollapsibleTopBar, double>(nameof(Reveal), 1);

    private double _contentHeight;

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

    private double Reveal
    {
        get => GetValue(RevealProperty);
        set => SetValue(RevealProperty, value);
    }

    public void SetExpanded(bool expanded, bool animate)
    {
        if (!animate || UiMotion.Reduced) Transitions = null;
        else Transitions ??= new Transitions
        {
            new DoubleTransition
            {
                Property = RevealProperty,
                Duration = TimeSpan.FromMilliseconds(180),
                Easing = new CubicEaseOut()
            }
        };
        Reveal = expanded ? 1 : 0;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Child?.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        var desired = Child?.DesiredSize ?? default;
        _contentHeight = desired.Height;
        return new Size(desired.Width, _contentHeight * Reveal);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(new Rect(0, finalSize.Height - _contentHeight, finalSize.Width, _contentHeight));
        return finalSize;
    }
}
