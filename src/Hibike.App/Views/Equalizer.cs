using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Hibike.App.Views;

internal sealed class Equalizer : Border
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(UiTheme.EqualizerMilliseconds) };
    private readonly Border[] _bars;
    private int _frame;
    private bool _playing;

    public Equalizer()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = UiTheme.EqualizerGap,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _bars = Enumerable.Range(0, 3).Select(_ => new Border { Width = UiTheme.EqualizerBarWidth,
            Height = UiTheme.CompactGap, Background = UiTheme.Brush(UiTheme.Primary), CornerRadius = new(UiTheme.ProgressRadius),
            VerticalAlignment = VerticalAlignment.Bottom }).ToArray();
        foreach (var bar in _bars) panel.Children.Add(bar);
        Child = panel; Background = UiTheme.Brush(UiTheme.Scrim); CornerRadius = new(UiTheme.ThumbRadius);
        IsHitTestVisible = false; IsVisible = false;
        _timer.Tick += (_, _) => { _frame++; for (var i = 0; i < _bars.Length; i++) _bars[i].Height = UiTheme.CompactGap + ((_frame + i * 2) % 4) * UiTheme.EqualizerStep; };
        DetachedFromVisualTree += (_, _) => _timer.Stop();
        AttachedToVisualTree += (_, _) => { if (_playing) _timer.Start(); };
    }
    public void SetState(bool selected, bool playing)
    {
        IsVisible = selected;
        _playing = selected && playing;
        if (_playing && TopLevel.GetTopLevel(this) is not null) _timer.Start(); else _timer.Stop();
    }
}
