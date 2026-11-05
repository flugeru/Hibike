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
    private void InitializeLoading(Grid header)
    {
        var row = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = UiTheme.CompactGap };
        row.Children.Add(_loadingProgress);
        Put(row, _loadingLabel, 0, 1);
        _loadingLabel.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        _loadingBanner = new Border
        {
            Child = row,
            Padding = new Thickness(0, 4, 0, 6),
            ClipToBounds = true,
            IsVisible = false,
            IsHitTestVisible = false
        };
        Put(header, _loadingBanner, 1, 0);
        Grid.SetColumnSpan(_loadingBanner, header.ColumnDefinitions.Count);
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
