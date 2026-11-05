using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Hibike.App.Views;

public sealed partial class MainView
{
    private readonly Grid _topMenuLayer = new() { IsVisible = false, Background = Brushes.Transparent };
    private readonly Border _topMenuPanel = new()
    {
        ClipToBounds = true, VerticalAlignment = VerticalAlignment.Top,
        Background = UiTheme.Brush(UiTheme.Surface), BorderBrush = UiTheme.Brush(UiTheme.Outline),
        BorderThickness = new Thickness(1), CornerRadius = new(16)
    };
    private bool _topMenuOpen;
    private int _topMenuVersion;
    private void InitializeTopMenu(Grid content)
    {
        _topMenuLayer.Children.Add(_topMenuPanel);
        _topMenuLayer.PointerPressed += (_, e) => { if (e.Source == _topMenuLayer) { CloseTopMenu(); e.Handled = true; } };
        Put(content, _topMenuLayer, 1, 0); Grid.SetRowSpan(_topMenuLayer, 2);
        _topMenuLayer.SizeChanged += (_, _) => { if (_topMenuOpen) SizeTopMenu(); };
    }
    private void SizeTopMenu()
    {
        if (_topMenuPanel.Child is not ScrollViewer scroll || scroll.Content is not StackPanel items) return;
        items.Measure(new Size(Math.Max(0, _main.Bounds.Width - 32), double.PositiveInfinity));
        _topMenuPanel.Height = Math.Min(items.DesiredSize.Height + 16, Math.Max(0, _topMenuLayer.Bounds.Height));
    }
    private void ToggleTopMenu()
    {
        if (_topMenuOpen) { CloseTopMenu(); return; }
        var items = new StackPanel { Spacing = 4, Margin = new Thickness(8) };
        void Add(string icon, string label, Func<Task> action)
        {
            var button = ActionButton("", async () => { CloseTopMenu(); ClosePlayer(); CloseSheet(); await action(); });
            button.Classes.Add("topMenuAction");
            var row = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 12, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(Icons.Make(icon, Muted, 22));
            var caption = Text(label, UiTheme.Body);
            caption.TextWrapping = TextWrapping.Wrap;
            Put(row, caption, 0, 1);
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            button.Content = row;
            Avalonia.Automation.AutomationProperties.SetName(button, label); items.Children.Add(button);
        }
        Add("account", "Minha conta", ShowAccountAsync);
        Add("settings", "Configurações", () => { ShowSettings(); return Task.CompletedTask; });
        Add("folder", "Importar músicas do dispositivo", OpenLocalAsync);
        Add("plus", "Nova playlist local", () => { ShowNewPlaylist(); return Task.CompletedTask; });
        if (_account is not null && _music.CanUseOnline)
            Add("refresh", "Atualizar biblioteca", LoadAccountHomeAsync);
        Add("heart", "Personalizar meu som", () => { ShowMusicOnboarding(edit: true); return Task.CompletedTask; });
        if (_music.CanUseOnline) Add("refresh", "Atualizar recomendações", () => _music.RefreshRecommendationsAsync(true));
        foreach (var playlist in _music.Playlists.Where(p => p.Pinned))
            Add("queue", playlist.Name, () => { ShowLocalPlaylist(playlist); return Task.CompletedTask; });
        _topMenuPanel.Child = new ScrollViewer { Content = items, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        _topMenuVersion++; _topMenuOpen = true; _topMenuLayer.IsVisible = true;
        SizeTopMenu();
        _topMenuPanel.IsHitTestVisible = true;
        _topMenuPanel.IsEnabled = true;
        UiMotion.Enter(_topMenuPanel, -12);
    }
    private async void CloseTopMenu()
    {
        if (!_topMenuOpen) return;
        var version = ++_topMenuVersion; _topMenuOpen = false;
        _topMenuPanel.IsHitTestVisible = false;
        _topMenuPanel.IsEnabled = false;
        await UiMotion.ExitAsync(_topMenuPanel, -12);
        if (version == _topMenuVersion) _topMenuLayer.IsVisible = false;
    }
}
