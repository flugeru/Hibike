using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Hibike.App.Views;

// Animate only overflow. Keep original TextBlock for accessibility and mobile reuse.
internal sealed class ScrollingTitle : Canvas
{
    private readonly TextBlock _text;
    private readonly TranslateTransform _offset = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(UiTheme.MarqueeFrameMilliseconds) };
    private double _distance, _position;
    private int _direction = 1;
    private double _pause;

    public ScrollingTitle(TextBlock text)
    {
        _text = text; ClipToBounds = true; Height = UiTheme.TitleLineHeight;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        text.RenderTransform = _offset; Children.Add(text); ToolTip.SetTip(this, text.Text);
        SizeChanged += (_, _) => Reset();
        AttachedToVisualTree += (_, _) => Reset();
        DetachedFromVisualTree += (_, _) => { _timer.Stop(); _offset.X = 0; };
        _timer.Tick += (_, _) =>
        {
            if (_pause > 0) { _pause--; return; }
            _position = Math.Clamp(_position + _direction * UiTheme.MarqueeStep, 0, _distance);
            _offset.X = -_position;
            if (_position == 0 || _position == _distance) { _direction = -_direction; _pause = UiTheme.MarqueePauseFrames; }
        };
    }

    private void Reset()
    {
        _timer.Stop(); _text.Measure(Size.Infinity); _text.Width = _text.DesiredSize.Width;
        _distance = Math.Max(0, _text.DesiredSize.Width - Bounds.Width);
        _position = 0; _offset.X = 0; _direction = 1; _pause = UiTheme.MarqueePauseFrames;
        if (_distance > 0 && TopLevel.GetTopLevel(this) is not null) _timer.Start();
    }
}
