using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Hibike.App.Views;

internal sealed class ScrollingTitle : Canvas
{
    private const double Speed = 32;
    private const double PauseSeconds = 1.8;
    private readonly TextBlock _text;
    private readonly TranslateTransform _offset = new();
    private DispatcherTimer? _timer;
    private double _distance, _position, _pause;
    private int _direction;
    private long _lastFrame;
    private bool _enabled;

    public ScrollingTitle(TextBlock text)
    {
        _text = text;
        ClipToBounds = true;
        text.TextWrapping = TextWrapping.NoWrap;
        text.MaxLines = 1;
        text.RenderTransform = _offset;
        Children.Add(text);
        ToolTip.SetTip(this, text.Text);

        text.PropertyChanged += (_, change) =>
        {
            if (change.Property == TextBlock.TextProperty) ToolTip.SetTip(this, text.Text);
            else if (change.Property != TextBlock.FontSizeProperty && change.Property != TextBlock.FontWeightProperty) return;
            Reset();
            InvalidateMeasure();
        };
        AttachedToVisualTree += (_, _) => { Reset(); InvalidateArrange(); };
        DetachedFromVisualTree += (_, _) => Reset();
    }

    private void Advance(object? sender, EventArgs args)
    {
        if (!CanScroll || !IsEffectivelyVisible)
        {
            Reset();
            InvalidateArrange();
            return;
        }
        var now = Stopwatch.GetTimestamp();
        var elapsed = Math.Min(.05, Stopwatch.GetElapsedTime(_lastFrame, now).TotalSeconds);
        _lastFrame = now;
        if (_pause > 0) { _pause -= elapsed; return; }
        _position = Math.Clamp(_position + _direction * Speed * elapsed, 0, _distance);
        _offset.X = FlowDirection == FlowDirection.RightToLeft ? _position : -_position;
        if (_position == 0 || _position == _distance)
        {
            _direction = -_direction;
            _pause = PauseSeconds;
        }
    }

    private bool CanScroll => _enabled && !UiMotion.Reduced;

    public void SetScrolling(bool enabled)
    {
        if (_enabled == enabled) return;
        _enabled = enabled;
        Reset();
        InvalidateArrange();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _text.Measure(Size.Infinity);
        return new Size(Math.Min(availableSize.Width, _text.DesiredSize.Width), _text.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var width = _text.DesiredSize.Width;
        var distance = CanScroll ? Math.Max(0, width - finalSize.Width) : 0;
        if (Math.Abs(distance - _distance) > .5) Reset();
        _distance = distance;
        var scrolling = distance > .5;
        _text.TextTrimming = scrolling ? TextTrimming.None : TextTrimming.CharacterEllipsis;
        _text.Arrange(new Rect(0, 0, scrolling ? width : finalSize.Width, finalSize.Height));
        if (scrolling && _timer?.IsEnabled != true && IsEffectivelyVisible && TopLevel.GetTopLevel(this) is not null)
        {
            if (_timer is null)
            {
                _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
                _timer.Tick += Advance;
            }
            _lastFrame = Stopwatch.GetTimestamp();
            _timer.Start();
        }
        return finalSize;
    }

    private void Reset()
    {
        _timer?.Stop();
        _position = 0;
        _offset.X = 0;
        _direction = 1;
        _pause = PauseSeconds;
    }
}
