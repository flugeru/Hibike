using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Hibike.App.Views;

public sealed partial class MainView
{
    private readonly Dictionary<object, string> _headerLoadingOperations = new();
    private readonly TextBlock _loadingLabel = Text("", UiTheme.Caption, Muted);
    private readonly ContentControl _loadingRing = new() { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center };
    private Border _loadingBanner = null!;
    private Control _headerTitle = null!;
    private string? _pullLoadingHint;
    private bool _lastHeaderReduceMotion;

    private void InitializeLoading(Grid header, Control title)
    {
        _headerTitle = title;
        Grid.SetRowSpan(title, 2);

        var row = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = UiTheme.CompactGap };
        row.Children.Add(_loadingRing);
        Put(row, _loadingLabel, 0, 1);
        _loadingLabel.VerticalAlignment = VerticalAlignment.Center;
        _loadingLabel.TextTrimming = TextTrimming.CharacterEllipsis;
        _loadingBanner = new Border
        {
            Child = row,
            ClipToBounds = true,
            IsVisible = false,
            IsHitTestVisible = false
        };
        Put(header, _loadingBanner, 1, 1);
    }

    private async Task WithHeaderLoadingAsync(string message, Func<Task> operation, bool keepFeedbackVisible = false)
    {
        var key = new object();
        _headerLoadingOperations.Add(key, message);
        RefreshHeaderLoading();
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        try { await operation(); }
        finally
        {
            var remaining = TimeSpan.FromMilliseconds(280) - System.Diagnostics.Stopwatch.GetElapsedTime(started);
            if (keepFeedbackVisible && remaining > TimeSpan.Zero && _music.CanUseOnline) await Task.Delay(remaining);
            _headerLoadingOperations.Remove(key);
            RefreshHeaderLoading();
        }
    }

    private void RefreshHeaderLoading()
    {
        var loading = _music.CanUseOnline ? _headerLoadingOperations.Values.LastOrDefault() : null;
        var message = loading ?? (_music.CanUseOnline ? _pullLoadingHint : null);
        var visible = message is not null;
        var wasVisible = _loadingBanner.IsVisible;
        _loadingLabel.Text = message ?? "";
        _loadingRing.IsVisible = loading is not null;
        if (loading is null)
            _loadingRing.Content = null;
        else if (_loadingRing.Content is null || _lastHeaderReduceMotion != _music.ReduceMotion)
            _loadingRing.Content = LoadingRing(14, Accent, animate: !_music.ReduceMotion);
        if (wasVisible != visible || _lastHeaderReduceMotion != _music.ReduceMotion)
            UiMotion.Move(_headerTitle, visible ? -10 : 0);
        _lastHeaderReduceMotion = _music.ReduceMotion;
        _loadingBanner.IsVisible = visible;
        if (visible && !wasVisible) UiMotion.Enter(_loadingBanner, 2, decorative: true);
    }
}
