using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Hibike.App.Views;

public sealed partial class MainView
{
    private readonly Dictionary<object, string> _loadingOperations = new();
    private readonly TextBlock _loadingLabel = Text("", UiTheme.Caption);
    private readonly ProgressBar _loadingProgress = new() { IsIndeterminate = true, Width = 48, Height = 4, VerticalAlignment = VerticalAlignment.Center };
    private Border _loadingBanner = null!;
    private void InitializeLoading(Grid content)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        row.Children.Add(_loadingProgress); row.Children.Add(_loadingLabel);
        _loadingBanner = UiTheme.Card(row, 12);
        _loadingBanner.Background = UiTheme.Brush(UiTheme.SurfaceElevated);
        _loadingBanner.BoxShadow = new Avalonia.Media.BoxShadows(new Avalonia.Media.BoxShadow { Blur = 16, OffsetY = 4, Color = Avalonia.Media.Color.Parse("#80000000") });
        _loadingBanner.IsVisible = false; _loadingBanner.IsHitTestVisible = false;
        _loadingBanner.ZIndex = 1000;
        _loadingLabel.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        _loadingLabel.MaxWidth = 240;
        _loadingBanner.HorizontalAlignment = HorizontalAlignment.Center;
        _loadingBanner.VerticalAlignment = VerticalAlignment.Top;
        _loadingBanner.Margin = new Thickness(12, 8);
        Grid.SetRow(_loadingBanner, 2);
        content.Children.Add(_loadingBanner);
    }
    private async Task WithLoadingAsync(string message, Func<Task> operation)
    {
        var key = new object();
        _loadingOperations.Add(key, message); RefreshLoading();
        try { await operation(); }
        finally { _loadingOperations.Remove(key); RefreshLoading(); }
    }
    private void RefreshLoading()
    {
        if (_loadingBanner is null) return;
        var message = _loadingOperations.Values.LastOrDefault(value => value is "Atualizando dados…" or "Restaurando conta Google…");
        _loadingLabel.Text = message ?? "";
        Avalonia.Automation.AutomationProperties.SetName(_loadingProgress, message ?? "Carregamento concluído");
        _loadingBanner.IsVisible = message is not null;
    }
}
