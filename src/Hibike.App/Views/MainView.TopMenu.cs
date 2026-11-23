using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Hibike.App.Views;

public sealed partial class MainView
{
    // A dimmed page behind an elevated sheet makes the menu read as a layer above the content.
    private readonly Grid _topMenuLayer = new() { IsVisible = false, Background = UiTheme.Brush("#99000000") };
    private readonly Border _topMenuPanel = new()
    {
        ClipToBounds = true, VerticalAlignment = VerticalAlignment.Top,
        Background = UiTheme.Brush("#161616"), BorderBrush = UiTheme.Brush("#262626"),
        BorderThickness = new Thickness(1), CornerRadius = new(18)
    };
    private bool _topMenuOpen;
    private int _topMenuVersion;
    private void InitializeTopMenu(Grid content)
    {
        _topMenuLayer.Children.Add(_topMenuPanel);
        _topMenuLayer.PointerPressed += (_, e) => { if (e.Source == _topMenuLayer) { CloseTopMenu(); e.Handled = true; } };
        Put(content, _topMenuLayer, 1, 0); Grid.SetRowSpan(_topMenuLayer, 2);
        // The sheet sizes to its items and scrolls past the available height. Computing a fixed
        // height from the hidden layer gave 0 on first open, so the sheet jumped open a frame later.
        _topMenuLayer.SizeChanged += (_, e) => _topMenuPanel.MaxHeight = Math.Max(0, e.NewSize.Height);
        // The scrim fades with the sheet instead of appearing and vanishing at once.
        _topMenuLayer.Transitions = [new Avalonia.Animation.DoubleTransition
            { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(180), Easing = new Avalonia.Animation.Easings.CubicEaseOut() }];
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
        _topMenuVersion++; _topMenuOpen = true;
        _topMenuPanel.IsHitTestVisible = _topMenuLayer.IsHitTestVisible = true;
        // Start the scrim from transparent without animating that reset, then fade it in.
        var fade = _topMenuLayer.Transitions;
        _topMenuLayer.Transitions = null;
        _topMenuLayer.Opacity = UiMotion.Reduced ? 1 : 0;
        _topMenuLayer.IsVisible = true;
        _topMenuLayer.Transitions = fade;
        _topMenuLayer.Opacity = 1;
        UiMotion.Enter(_topMenuPanel, -12);
    }
    private async void CloseTopMenu()
    {
        if (!_topMenuOpen) return;
        var version = ++_topMenuVersion; _topMenuOpen = false;
        // Only stop input: disabling the sheet restyled every item to 40 % opacity before the fade.
        _topMenuPanel.IsHitTestVisible = false;
        _topMenuLayer.IsHitTestVisible = false;
        if (UiMotion.Reduced) { var fade = _topMenuLayer.Transitions; _topMenuLayer.Transitions = null; _topMenuLayer.Opacity = 0; _topMenuLayer.Transitions = fade; }
        else _topMenuLayer.Opacity = 0;
        await UiMotion.ExitAsync(_topMenuPanel, -12);
        if (version == _topMenuVersion) _topMenuLayer.IsVisible = false;
    }
}
