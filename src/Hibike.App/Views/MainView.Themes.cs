using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Track = Hibike.Core.Models.Track;

namespace Hibike.App.Views;

public sealed partial class MainView
{
    private CancellationTokenSource? _coverColorRequest;
    private string? _coverColorSource;
    private Color? _playerTint;

    private void InitializeThemes()
    {
        // Theme and accent are process-wide; only an attached view listens, so discarded views never leak.
        AttachedToVisualTree += (_, _) =>
        {
            UiTheme.AccentChanged -= OnAccentChanged; UiTheme.AccentChanged += OnAccentChanged;
            _music.ThemeChanged -= ApplyTheme; _music.ThemeChanged += ApplyTheme;
            ApplyTheme();
        };
        DetachedFromVisualTree += (_, _) => { UiTheme.AccentChanged -= OnAccentChanged; _music.ThemeChanged -= ApplyTheme; };
    }

    private void ApplyTheme()
    {
        if (_music.Theme == UiTheme.ThemeCover)
        {
            _coverColorSource = null;
            UpdateCoverTheme(_music.Pending ?? _music.Current);
            return;
        }
        _coverColorRequest?.Cancel();
        _coverColorSource = null;
        _playerTint = null;
        UiTheme.SetAccent(UiTheme.ThemeAccent(_music.Theme));
        UpdatePlayerTint();
    }

    private void UpdateCoverTheme(Track? track)
    {
        if (_music.Theme != UiTheme.ThemeCover) return;
        var url = track?.Thumbnail;
        if (url == _coverColorSource && _coverColorSource is not null) return;
        _coverColorSource = url;
        _coverColorRequest?.Cancel();
        if (string.IsNullOrWhiteSpace(url)) { ApplyCoverColors(null); return; }
        var request = _coverColorRequest = new CancellationTokenSource();
        _ = LoadAsync();
        async Task LoadAsync()
        {
            CoverPalette.Colors? colors = null;
            try
            {
                using var lease = await ArtworkCache.AcquireAsync(url, url, 64, request.Token);
                if (request.IsCancellationRequested) return;
                colors = CoverPalette.From(lease.Bitmap);
            }
            catch (Exception) when (!request.IsCancellationRequested) { /* Without a readable cover the monochrome accent stays. */ }
            catch (OperationCanceledException) { return; }
            if (!request.IsCancellationRequested && ReferenceEquals(_coverColorRequest, request)) ApplyCoverColors(colors);
        }
    }

    private void ApplyCoverColors(CoverPalette.Colors? colors)
    {
        _playerTint = colors?.Tint;
        UiTheme.SetAccent(colors?.Accent ?? UiTheme.Primary);
        UpdatePlayerTint();
    }

    private void OnAccentChanged()
    {
        // Rebuild the visuals that baked the previous accent in: tab indicators and toggles.
        foreach (var button in _nav) button.Content = "";
        foreach (var button in new[] { _dockShuffle, _detailShuffle, _dockRepeat, _detailRepeat, _dockFavorite, _detailFavorite })
            if (button is not null) button.Tag = null;
        Refresh();
        RefreshPlaybackActions();
    }

    // "Cor da capa" washes the expanded player from the cover's hue into the AMOLED black.
    private void UpdatePlayerTint()
    {
        if (_playerPage is null) return;
        _playerPage.Background = _playerTint is { } tint
            ? new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = [new GradientStop(tint, 0), new GradientStop(Color.Parse(UiTheme.Background), .72)]
            }
            : UiTheme.Brush(UiTheme.Background);
    }

    private Control ThemePicker()
    {
        var options = new WrapPanel { ItemSpacing = UiTheme.CompactGap, LineSpacing = UiTheme.CompactGap };
        var note = Text("", UiTheme.Caption, Muted);
        note.TextWrapping = TextWrapping.Wrap;
        var buttons = new List<(string Theme, Button Button)>();
        void Select()
        {
            foreach (var (theme, button) in buttons) button.Classes.Set("primary", theme == _music.Theme);
            note.Text = _music.Theme switch
            {
                UiTheme.ThemeCover => "O destaque e o fundo do player seguem a cor da capa que está tocando.",
                UiTheme.ThemeMonochrome => "Visual original do Hibike, em preto e branco.",
                _ => "Destaque em " + _music.Theme.ToLowerInvariant() + " para reprodução, seleção e progresso."
            };
        }
        foreach (var theme in UiTheme.Themes)
        {
            var swatch = new Border { Width = 14, Height = 14, CornerRadius = new(7), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                BorderBrush = UiTheme.Brush(UiTheme.Outline), BorderThickness = UiTheme.One,
                Background = theme == UiTheme.ThemeCover
                    ? new LinearGradientBrush
                    {
                        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                        GradientStops = [new GradientStop(Color.Parse("#F2A65A"), 0), new GradientStop(Color.Parse("#B784F7"), .5), new GradientStop(Color.Parse("#5AC8F2"), 1)]
                    }
                    : UiTheme.Brush(UiTheme.ThemeAccent(theme)) };
            var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(swatch); row.Children.Add(new TextBlock { Text = theme, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
            var button = ActionButton("", () => { _music.SetTheme(theme); Select(); return Task.CompletedTask; });
            button.Content = row; button.Classes.Add("pill"); button.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            Avalonia.Automation.AutomationProperties.SetName(button, "Tema " + theme);
            buttons.Add((theme, button)); options.Children.Add(button);
        }
        Select();
        var panel = new StackPanel { Spacing = UiTheme.CompactGap };
        panel.Children.Add(Text("Tema", UiTheme.Body)); panel.Children.Add(options); panel.Children.Add(note);
        return panel;
    }
}
