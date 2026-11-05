using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Hibike.App.Views;

public sealed partial class MainView
{
    private readonly Dictionary<object, string> _headerLoadingOperations = new();
    private readonly TextBlock _loadingLabel = Text("", UiTheme.Caption, Muted);
    private readonly ProgressBar _loadingProgress = new()
    {
        Width = 32, Height = 4, Value = 50, VerticalAlignment = VerticalAlignment.Center
    };
    private Border _loadingBanner = null!;
    private Control _headerTitle = null!;

    private void InitializeLoading(Grid header, Control title)
    {
        _headerTitle = title;
        Grid.SetRowSpan(title, 2);

        var row = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = UiTheme.CompactGap };
        row.Children.Add(_loadingProgress);
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
        Put(header, _loadingBanner, 1, 0);
    }

    private async Task WithHeaderLoadingAsync(string message, Func<Task> operation)
    {
        var key = new object();
        _headerLoadingOperations.Add(key, message);
        RefreshHeaderLoading();
        try { await operation(); }
        finally
        {
            _headerLoadingOperations.Remove(key);
            RefreshHeaderLoading();
        }
    }

    private void RefreshHeaderLoading()
    {
        var message = _headerLoadingOperations.Values.LastOrDefault();
        var visible = message is not null;
        _loadingLabel.Text = message ?? "";
        AutomationProperties.SetName(_loadingProgress, message ?? "");
        _loadingProgress.IsIndeterminate = visible && !_music.ReduceMotion;
        Grid.SetRowSpan(_headerTitle, visible ? 1 : 2);
        _loadingBanner.IsVisible = visible;
    }
}
