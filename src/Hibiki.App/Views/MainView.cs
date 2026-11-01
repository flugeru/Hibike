using Avalonia;
using Avalonia.Animation;
using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Styling;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Hibiki.Core.Models;
using Hibiki.Core.Services;
using Track = Hibiki.Core.Models.Track;

namespace Hibiki.App.Views;

public sealed class MainView : UserControl
{
    private readonly MusicController _music;
    private readonly Grid _root = new() { ColumnDefinitions = new("200,*"), RowDefinitions = new("*,Auto,Auto") };
    private readonly Border _sidebar;
    private readonly Border _main;
    private readonly TextBox _search = new() { PlaceholderText = "Música, artista ou link", MinWidth = 0, Height = 48, MinHeight = 48, FontSize = 14 };
    private readonly TextBlock _heading = Text("Buscar músicas", 18);
    private readonly TextBlock _count = Text("", 12, "TextTertiaryBrush");
    private readonly ListBox _trackList = new() { SelectionMode = SelectionMode.Single, Padding = new Thickness(0) };
    private readonly StackPanel _empty = new() { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _status = Text("", 12, Muted);
    private readonly Border _statusBox;
    private readonly Button _clearSearch;
    private readonly Border _mini;
    private readonly ContentControl _nowArt = new();
    private readonly TextBlock _nowTitle = Text("", Ui.D("TypeTitle"));
    private readonly TextBlock _nowArtist = Text("", Ui.D("TypeCaption"), Muted);
    private readonly Button _play;
    private readonly ProgressBar _progress = new() { Height = Ui.D("ProgressHeight"), Minimum = 0, Maximum = 1, Foreground = Ui.B(Accent), Background = Ui.B("TrackBrush"), CornerRadius = Ui.Radius("RadiusPill") };
    private readonly Grid _bottom;
    private readonly List<Button> _nav = [];
    private Border? _overlay;
    private Border? _playerPage;
    private readonly Dictionary<string, (Border Surface, TextBlock Title, Border Equalizer)> _rows = new();
    private int _equalizerFrame;
    private string? _listFingerprint;
    private bool _showingPlayer;
    private Slider? _detailSeek;
    private TextBlock? _detailTime;
    private TextBlock? _detailDuration;
    private Button? _detailPlay;
    private bool _seeking;
    private Track? _shownTrack;
    private Track? _detailTrack;
    private string _page = "explore";
    private const string Muted = "TextSecondaryBrush", Accent = "AccentBrush";

    public MainView(MusicController music)
    {
        _music = music;
        Background = Ui.B("BackgroundBrush");
        TopLevel.SetAutoSafeAreaPadding(this, true);
        TopLevel.SetSystemBarColor(this, (SolidColorBrush)Ui.B("BackgroundBrush"));
        _sidebar = BuildSidebar(); _root.Children.Add(_sidebar); Grid.SetRowSpan(_sidebar, 3);
        _main = new Border { Padding = new Thickness(16, 10, 16, 4) }; Put(_root, _main, 0, 1);
        var content = new Grid { RowDefinitions = new("Auto,Auto,Auto,*,Auto"), RowSpacing = 12 }; _main.Child = content;
        // One balanced toolbar: the actions stay grouped, even on narrow phones.
        var header = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 12, MinHeight = 48, Margin = new Thickness(0, 0, 0, 4) };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9, VerticalAlignment = VerticalAlignment.Center };
        brand.Children.Add(Icons.Make("wave", Accent, 22));
        var title = Text("Hibiki", 21); title.FontWeight = FontWeight.SemiBold;
        title.VerticalAlignment = VerticalAlignment.Center; brand.Children.Add(title);
        header.Children.Add(brand);
        var headerActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        headerActions.Children.Add(IconButton("folder", "Abrir áudio local", OpenLocalAsync));
        headerActions.Children.Add(IconButton("settings", "Configurações e diagnóstico", () => { ShowSettings(); return Task.CompletedTask; }));
        Put(header, headerActions, 0, 1);
        content.Children.Add(header);
        _search.Height = _search.MinHeight = Ui.D("TouchTarget");
        _search.VerticalContentAlignment = VerticalAlignment.Center;
        _search.InnerLeftContent = Icons.Make("search", "TextSecondaryBrush", Ui.D("IconSize"));
        _clearSearch = IconButton("close", "Limpar pesquisa", () =>
        {
            if (_music.Searching) _music.CancelSearch();
            _search.Text = "";
            _search.Focus();
            return Task.CompletedTask;
        }, "TextSecondaryBrush");
        _clearSearch.IsVisible = false;
        _search.InnerRightContent = _clearSearch;
        _search.TextChanged += (_, _) => _clearSearch.IsVisible = !string.IsNullOrEmpty(_search.Text);
        _search.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; await SearchAsync(); }
        };
        Put(content, _search, 1, 0);

        var heading = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = Ui.D("Space8"),
            Margin = Ui.Insets("Space0", "Space12", "Space0", "Space0") };
        _heading.FontSize = Ui.D("TypeHeading"); _heading.TextTrimming = TextTrimming.CharacterEllipsis;
        _heading.FontWeight = FontWeight.SemiBold;
        _count.FontSize = Ui.D("TypeCaption");
        _count.VerticalAlignment = VerticalAlignment.Center;
        _count.HorizontalAlignment = HorizontalAlignment.Right;
        heading.Children.Add(_heading); Put(heading, _count, 0, 1);
        Put(content, heading, 2, 0);

        // ListBox keeps its own constrained viewport and virtualizes off-screen rows.
        _trackList.ItemTemplate = new FuncDataTemplate<Track>((track, _) => TrackRow(track));
        _trackList.HorizontalAlignment = HorizontalAlignment.Stretch;
        _trackList.VerticalAlignment = VerticalAlignment.Stretch;
        _trackList.MinHeight = 0;
        var listHost = new Grid { ClipToBounds = true };
        listHost.Children.Add(_trackList);
        _empty.Spacing = Ui.D("Space12");
        _empty.Margin = Ui.Insets("Space16", "Space24", "Space16", "Space0");
        listHost.Children.Add(_empty);
        var fade = new Border { IsHitTestVisible = false, Height = Ui.D("Space16"),
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops =
                [
                    new GradientStop(Ui.Color("TransparentBrush"), 0),
                    new GradientStop(Ui.Color("BackgroundBrush"), 1)
                ]
            }
        };
        listHost.Children.Add(fade);
        Put(content, listHost, 3, 0);
        var statusRow = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 };
        _status.TextWrapping = TextWrapping.Wrap; _status.MaxLines = 2; _status.VerticalAlignment = VerticalAlignment.Center; statusRow.Children.Add(_status);
        Put(statusRow, IconButton("info", "Ver diagnóstico", () => { ShowDiagnostics(); return Task.CompletedTask; }), 0, 1);
        _statusBox = new Border { Child = statusRow, Padding = new Thickness(10, 2), Background = Ui.B("SurfaceBrush"), CornerRadius = new CornerRadius(10) }; Put(content, _statusBox, 4, 0);
        _play = IconButton("play", "Reproduzir, pausar ou cancelar", _music.ToggleAsync, "OnAccentBrush");
        _play.Classes.Remove("subtle"); _play.Classes.Add("primary");
        _play.CornerRadius = Ui.Radius("RadiusPill");
        _mini = BuildMiniPlayer(); Put(_root, _mini, 1, 1);
        _bottom = new Grid { ColumnDefinitions = new("*,*,*"), Height = Ui.D("NavHeight"), Background = Ui.B("SurfaceBrush") };
        var tabs = new[] { ("explore", "Buscar", "search"), ("library", "Biblioteca", "library"), ("queue", "Fila", "queue") };
        for (var index = 0; index < tabs.Length; index++)
        {
            var (key, label, icon) = tabs[index];
            var button = NavButton(key, label, icon, true); Put(_bottom, button, 0, index);
        }
        Put(_root, _bottom, 2, 1); Content = _root;
        SizeChanged += (_, _) =>
        {
            var compact = Bounds.Width < 800;
            _root.ColumnDefinitions[0].Width = new GridLength(compact ? 0 : 200);
            _sidebar.IsVisible = !compact; _bottom.IsVisible = compact;
            _main.IsVisible = !_showingPlayer;
            _bottom.IsVisible = compact && !_showingPlayer;
            _main.Padding = compact ? new Thickness(16, 10, 16, 4) : new Thickness(32, 24, 32, 12);
        };
        AppServices.BackHandler = HandleBack; AppServices.OpenPlayerView = ShowPlayer;
        AttachedToVisualTree += async (_, _) =>
        {
            _music.Changed -= Refresh; _music.Tick -= RefreshPlayback;
            _music.Changed += Refresh; _music.Tick += RefreshPlayback;
            _music.StartObserving(); _listFingerprint = null; Refresh(); await _music.InitializeAsync();
        };
        DetachedFromVisualTree += (_, _) => { _music.Changed -= Refresh; _music.Tick -= RefreshPlayback; _music.StopObserving(); };
        Refresh();
    }
    private Border BuildSidebar()
    {
        var panel = new StackPanel { Spacing = 8, Margin = new Thickness(14, 30) };
        var title = Text("Sua música", 18); title.Margin = new Thickness(12, 0, 0, 22); panel.Children.Add(title);
        panel.Children.Add(NavButton("explore", "Buscar", "search", false));
        panel.Children.Add(NavButton("library", "Biblioteca", "heart", false));
        panel.Children.Add(NavButton("queue", "Fila", "queue", false));
        return new Border { Child = panel, Background = Ui.B("SurfaceBrush") };
    }
    private Button NavButton(string key, string label, string icon, bool vertical)
    {
        var button = ActionButton("", () => { _page = key; Refresh(); Animate(_trackList); return Task.CompletedTask; });
        button.Classes.Add("nav"); button.Classes.Add(vertical ? "bottom-nav" : "side-nav");
        button.Tag = (key, label, icon, vertical); _nav.Add(button);
        button.Padding = vertical ? new Thickness(8, 4) : new Thickness(12, 8);
        button.HorizontalContentAlignment = vertical ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        return button;
    }
    private Border BuildMiniPlayer()
    {
        var grid = new Grid { RowDefinitions = new("Auto,Auto"), ClipToBounds = true };
        var row = new Grid { ColumnDefinitions = new("*,Auto,Auto"),
            ColumnSpacing = Ui.D("Space4"), Margin = Ui.Insets("Space8", "Space8") };
        var open = ActionButton("", () => { ShowPlayer(); return Task.CompletedTask; });
        Avalonia.Automation.AutomationProperties.SetName(open, "Abrir player");
        open.Classes.Add("track"); open.Padding = Ui.Inset("Space0"); open.MinWidth = 0;
        open.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        var metadata = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = Ui.D("Space8"), MinWidth = 0 };
        _nowArt.Content = new Artwork(null, Ui.D("MiniThumbSize")); metadata.Children.Add(_nowArt);
        var info = new Grid { RowDefinitions = new("Auto,Auto"), RowSpacing = Ui.D("Space4"), MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center };
        _nowTitle.FontSize = Ui.D("TypeTitle"); _nowArtist.FontSize = Ui.D("TypeCaption");
        _nowTitle.TextTrimming = _nowArtist.TextTrimming = TextTrimming.CharacterEllipsis;
        _nowTitle.MaxLines = _nowArtist.MaxLines = 1;
        info.Children.Add(_nowTitle); Put(info, _nowArtist, 1, 0);
        Put(metadata, info, 0, 1); open.Content = metadata; row.Children.Add(open);
        Put(row, _play, 0, 1);
        Put(row, IconButton("next", "Próxima faixa", _music.NextAsync), 0, 2);
        grid.Children.Add(row);
        _progress.Margin = Ui.Insets("Space8", "Space0", "Space8", "Space8");
        Put(grid, _progress, 1, 0);
        return new Border
        {
            Child = grid, Margin = Ui.Insets("PagePadding", "Space8"),
            CornerRadius = Ui.Radius("RadiusCard"), ClipToBounds = true,
            Background = Ui.B("SurfaceElevatedBrush"),
            BorderThickness = new Thickness(Ui.D("TrackBorder")), BorderBrush = Ui.B("BorderBrush")
        };
    }
    private async Task SearchAsync() { _page = "explore"; await _music.SearchAsync(_search.Text ?? ""); }
    private async Task OpenLocalAsync()
    {
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top is null || !top.StorageProvider.CanOpen) { _music.SetStatus("Seletor de arquivos indisponível."); return; }
            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Escolha suas músicas", AllowMultiple = true,
                FileTypeFilter = [new FilePickerFileType("Áudio") { Patterns = ["*.mp3", "*.m4a", "*.aac", "*.ogg", "*.opus", "*.flac", "*.wav"], MimeTypes = ["audio/*"] }]
            });
            Track? first = null;
            foreach (var file in files)
            {
                // Import into private app storage so Android URI permissions cannot expire.
                var fileName = file.Name;
                var id = Guid.NewGuid().ToString("N");
                var directory = System.IO.Path.Combine(_music.DataDirectory, "audio"); System.IO.Directory.CreateDirectory(directory);
                var path = System.IO.Path.Combine(directory, id + System.IO.Path.GetExtension(fileName));
                try
                {
                    await using var input = await file.OpenReadAsync();
                    await using var output = System.IO.File.Create(path);
                    await input.CopyToAsync(output);
                }
                catch { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); throw; }
                finally { file.Dispose(); }
                var track = new Track(id, System.IO.Path.GetFileNameWithoutExtension(fileName), "Áudio local", path, IsLocal: true);
                _music.Enqueue(track); await _music.ToggleFavoriteAsync(track); first ??= track;
            }
            _page = "library";
            if (first is not null) await _music.PlayAsync(first);
            Refresh();
        }
        catch (Exception ex) { _music.SetStatus("Não foi possível abrir o áudio: " + ex.Message); }
    }

    private void Refresh()
    {
        _statusBox.IsVisible = _music.Searching || _music.LoadingAudio || _music.HasError;
        _status.Text = _music.Status;
        _clearSearch.IsVisible = !string.IsNullOrEmpty(_search.Text);
        foreach (var button in _nav)
        {
            var (key, label, icon, vertical) = ((string, string, string, bool))button.Tag!;
            var active = key == _page;
            button.Classes.Set("active", active);
            var stack = new StackPanel
            {
                Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal,
                Spacing = Ui.D("Space4"),
                HorizontalAlignment = vertical ? HorizontalAlignment.Center : HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (vertical)
            {
                var pill = new Border
                {
                    Width = Ui.D("IndicatorWidth"),
                    Height = Ui.D("IndicatorHeight"),
                    CornerRadius = Ui.Radius("RadiusPill"),
                    Background = active ? Ui.B("SurfaceElevatedBrush") : Ui.B("TransparentBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Child = Icons.Make(icon, active ? Accent : Muted, Ui.D("IconSize"))
                };
                stack.Children.Add(pill);
            }
            else
            {
                var image = Icons.Make(icon, active ? Accent : Muted, Ui.D("IconSize"));
                stack.Children.Add(image);
            }
            var name = Text(label, Ui.D("TypeNav"), active ? Accent : Muted);
            name.VerticalAlignment = VerticalAlignment.Center;
            if (vertical) name.TextAlignment = TextAlignment.Center;
            stack.Children.Add(name);
            button.Content = stack;
        }
        IReadOnlyList<Track> tracks = _page switch { "library" => _music.Favorites, "queue" => _music.Queue, _ => _music.Results };
        _heading.Text = _page switch { "library" => "Biblioteca", "queue" => "Fila de reprodução", _ => tracks.Count > 0 ? "Resultados" : "Encontre sua música" };
        _count.Text = tracks.Count > 0 ? tracks.Count + " faixas" : "";
        var fingerprint = _page + ":" + string.Join("|", tracks.Select(t => t.Id + _music.IsFavorite(t)));
        if (fingerprint != _listFingerprint)
        {
            _listFingerprint = fingerprint;
            _rows.Clear();
            _empty.Children.Clear();
            _trackList.ItemsSource = tracks.Count > 0 ? tracks.ToArray() : null;
            _trackList.IsVisible = tracks.Count > 0;
            _empty.IsVisible = tracks.Count == 0;
            if (tracks.Count == 0)
            {
                var icon = Icons.Make(_page == "library" ? "library" : "wave", "TextSecondaryBrush", Ui.D("IconSize"));
                icon.HorizontalAlignment = HorizontalAlignment.Center;
                _empty.Children.Add(icon);
                var message = Text(_page switch
                {
                    "library" => "Suas favoritas ficam aqui.",
                    "queue" => "Adicione músicas à fila.",
                    _ => "Busque uma música ou artista\npara começar a ouvir."
                }, Ui.D("TypeBody"), "TextSecondaryBrush");
                message.TextAlignment = TextAlignment.Center; _empty.Children.Add(message);
                var import = ActionButton("Abrir áudio local", OpenLocalAsync);
                import.Width = Ui.D("SidebarWidth");
                import.HorizontalAlignment = HorizontalAlignment.Center;
                import.Background = Ui.B("SurfaceBrush");
                _empty.Children.Add(import);
            }
        }
        var displayed = _music.Pending ?? _music.Current;
        _mini.IsVisible = displayed is not null && !_showingPlayer;
        foreach (var (id, row) in _rows)
            UpdateRowState(id, row, displayed);
        if (!Equals(displayed, _shownTrack))
        {
            _shownTrack = displayed;
            _nowArt.Content = new Artwork(displayed, Ui.D("MiniThumbSize"));
        }
        _nowTitle.Text = displayed is null ? "" : TrackTextFormatter.Title(displayed.Title, displayed.Artist);
        _nowArtist.Text = _music.LoadingAudio ? _music.Status
            : displayed is null ? "" : TrackTextFormatter.Artist(displayed.Artist);
        if (_detailPlay is not null && displayed is not null && !Equals(_detailTrack, displayed)) ShowPlayer();
        RefreshPlayback();
    }
    private void UpdateRowState(string id, (Border Surface, TextBlock Title, Border Equalizer) row, Track? current)
    {
        var active = current?.Id == id;
        row.Surface.Background = active ? Ui.B("SurfaceElevatedBrush") : Ui.B("TransparentBrush");
        row.Surface.BorderBrush = active ? Ui.B("BorderBrush") : Ui.B("TransparentBrush");
        row.Title.Foreground = active ? Ui.B("AccentBrush") : Ui.B("TextPrimaryBrush");
        row.Equalizer.IsVisible = active;
        // A subtle pulse while playing; paused keeps the equalizer still.
        row.Equalizer.Opacity = active && _music.Player.IsPlaying && _equalizerFrame % 2 == 0 ? 0.7 : 1;
    }

    private Control TrackRow(Track track)
    {
        var row = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = Ui.D("Space4"),
            Height = Ui.D("RowHeight"), MinWidth = 0 };
        var play = ActionButton("", async () => await _music.PlayAsync(track));
        play.Classes.Add("track"); play.Padding = Ui.Inset("Space0");
        play.MinWidth = 0; play.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Avalonia.Automation.AutomationProperties.SetName(play,
            TrackTextFormatter.Title(track.Title, track.Artist) + " — " + TrackTextFormatter.Artist(track.Artist));

        var metadata = new Grid { ColumnDefinitions = new("Auto,*,Auto"),
            ColumnSpacing = Ui.D("Space8"), MinWidth = 0 };
        var cover = new Grid { Width = Ui.D("ThumbSize"), Height = Ui.D("ThumbSize") };
        cover.Children.Add(new Artwork(track, Ui.D("ThumbSize"))
        { VerticalAlignment = VerticalAlignment.Center });
        var equalizer = new Border
        {
            Child = Icons.Make("equalizer", "AccentBrush", Ui.D("IconSize")),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            CornerRadius = Ui.Radius("RadiusThumb"),
            Background = Ui.B("SurfaceElevatedBrush"),
            IsVisible = false
        };
        cover.Children.Add(equalizer);
        metadata.Children.Add(cover);

        var labels = new Grid { RowDefinitions = new("Auto,Auto"),
            RowSpacing = Ui.D("Space4"), MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center };
        var title = Text(TrackTextFormatter.Title(track.Title, track.Artist),
            Ui.D("TypeTitle"), "TextPrimaryBrush");
        title.TextTrimming = TextTrimming.CharacterEllipsis; title.MaxLines = 1;
        title.FontWeight = FontWeight.SemiBold;
        labels.Children.Add(title);
        var artist = Text(TrackTextFormatter.Artist(track.Artist),
            Ui.D("TypeCaption"), "TextSecondaryBrush");
        artist.TextTrimming = TextTrimming.CharacterEllipsis; artist.MaxLines = 1;
        Put(labels, artist, 1, 0); Put(metadata, labels, 0, 1);

        var duration = Text(track.DurationLabel, Ui.D("TypeCaption"), "TextTertiaryBrush");
        duration.TextTrimming = TextTrimming.CharacterEllipsis;
        duration.VerticalAlignment = VerticalAlignment.Center;
        duration.HorizontalAlignment = HorizontalAlignment.Right;
        Put(metadata, duration, 0, 2);

        play.Content = metadata; row.Children.Add(play);
        var more = IconButton("more", "Opções de " + track.Title,
            () => Task.CompletedTask, "TextSecondaryBrush");
        more.VerticalAlignment = VerticalAlignment.Center;
        var favorite = new MenuItem { Header = _music.IsFavorite(track)
            ? "Remover dos favoritos" : "Salvar nos favoritos" };
        favorite.Click += async (_, _) => await _music.ToggleFavoriteAsync(track);
        var queue = new MenuItem { Header = "Adicionar à fila" };
        queue.Click += (_, _) => _music.Enqueue(track);
        var menu = new ContextMenu { ItemsSource = new[] { favorite, queue } };
        more.Click += (_, _) => menu.Open(more);
        Put(row, more, 0, 1);

        var surface = new Border
        {
            Child = row,
            CornerRadius = Ui.Radius("RadiusCard"),
            Margin = Ui.Insets("Space0", "Space4"),
            BorderThickness = new Thickness(Ui.D("TrackBorder")),
            BorderBrush = Ui.B("TransparentBrush"),
            ClipToBounds = true
        };
        var rendered = (surface, title, equalizer);
        surface.AttachedToVisualTree += (_, _) =>
        {
            _rows[track.Id] = rendered;
            UpdateRowState(track.Id, rendered, _music.Pending ?? _music.Current);
        };
        surface.DetachedFromVisualTree += (_, _) =>
        {
            if (_rows.TryGetValue(track.Id, out var live) && ReferenceEquals(live.Surface, surface))
                _rows.Remove(track.Id);
        };
        return surface;
    }
    private void ShowPlayer()
    {
        var track = _music.Pending ?? _music.Current;
        if (track is null) return;
        if (_overlay is not null) CloseSheet();
        _detailTrack = track; _showingPlayer = true;
        if (_playerPage is not null) _root.Children.Remove(_playerPage);

        var width = Math.Min(Ui.D("MaxContentWidth"), Math.Max(0,
            Bounds.Width - Ui.D("PagePadding") * 2));
        var layout = new Grid
        {
            Width = width,
            MinHeight = Math.Max(0, Bounds.Height - Ui.D("Space24")),
            RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto,Auto,Auto,*,Auto"),
            RowSpacing = Ui.D("Space12"),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var header = new Grid { ColumnDefinitions = new("Auto,*,Auto"),
            MinHeight = Ui.D("TouchTarget") };
        var collapse = IconButton("chevron-down", "Recolher player", () =>
        {
            ClosePlayer(); return Task.CompletedTask;
        });
        header.Children.Add(collapse);

        // There is no album field in Track yet; display its artist instead of inventing an album.
        var sourceLabel = Text(TrackTextFormatter.Artist(track.Artist),
            Ui.D("TypeCaption"), "TextSecondaryBrush");
        sourceLabel.TextTrimming = TextTrimming.CharacterEllipsis; sourceLabel.MaxLines = 1;
        sourceLabel.VerticalAlignment = VerticalAlignment.Center;
        sourceLabel.HorizontalAlignment = HorizontalAlignment.Center;
        Put(header, sourceLabel, 0, 1);
        Put(header, IconButton("queue", "Abrir fila", () =>
        {
            ClosePlayer(); _page = "queue"; Refresh(); return Task.CompletedTask;
        }), 0, 2);
        Put(layout, header, 0, 0);

        var artSize = Math.Max(0, Math.Min(width, Bounds.Height * Ui.D("PlayerArtworkRatio")));
        var artView = new Artwork(track, artSize)
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            BoxShadow = Ui.Shadows("CoverShadow")
        };
        Put(layout, artView, 2, 0);

        var metadata = new Grid { ColumnDefinitions = new("*,Auto"),
            ColumnSpacing = Ui.D("Space8"), MinWidth = 0 };
        var labels = new StackPanel { Spacing = Ui.D("Space4"), MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center };
        var title = Text(TrackTextFormatter.Title(track.Title, track.Artist),
            Ui.D("TypePlayerTitle"));
        title.FontWeight = FontWeight.Bold;
        title.TextWrapping = TextWrapping.Wrap; title.MaxLines = 2;
        labels.Children.Add(title);
        var artist = Text(TrackTextFormatter.Artist(track.Artist),
            Ui.D("TypeArtist"), "TextSecondaryBrush");
        artist.TextTrimming = TextTrimming.CharacterEllipsis; artist.MaxLines = 1;
        labels.Children.Add(artist); metadata.Children.Add(labels);
        Button? favorite = null;
        favorite = IconButton(_music.IsFavorite(track) ? "heart" : "heart-outline",
            "Alternar favorito", async () =>
            {
                await _music.ToggleFavoriteAsync(track);
                var isFavorite = _music.IsFavorite(track);
                favorite!.Content = Icons.Make(isFavorite ? "heart" : "heart-outline",
                    isFavorite ? "AccentBrush" : "TextSecondaryBrush", Ui.D("IconSize"));
                // A short feedback transition, without moving the hit target.
                favorite.Opacity = Ui.D("FavoritePressedOpacity");
                Dispatcher.UIThread.Post(() => favorite.Opacity = 1, DispatcherPriority.Render);
            }, _music.IsFavorite(track) ? "AccentBrush" : "TextSecondaryBrush");
        favorite.Transitions = new Transitions
        {
            new DoubleTransition { Property = OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(Ui.D("AnimationMs")) }
        };
        Put(metadata, favorite, 0, 1);
        Put(layout, metadata, 3, 0);

        var seekGroup = new StackPanel { Spacing = Ui.D("Space4") };
        _detailSeek = new Slider
        {
            Minimum = 0, Maximum = 1,
            Height = Ui.D("TouchTarget"),
            Theme = Ui.Get<ControlTheme>("HibikiSeekSlider"),
            Margin = Ui.Inset("Space0")
        };
        _detailSeek.AddHandler(PointerPressedEvent, (_, _) => _seeking = true,
            Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _detailSeek.AddHandler(PointerReleasedEvent, (_, _) =>
        {
            _seeking = false;
            _music.Player.Seek(_detailSeek.Value);
        }, Avalonia.Interactivity.RoutingStrategies.Bubble, true);
        _detailSeek.KeyUp += (_, _) => _music.Player.Seek(_detailSeek.Value);
        seekGroup.Children.Add(_detailSeek);
        var times = new Grid { ColumnDefinitions = new("*,Auto") };
        _detailTime = Text("", Ui.D("TypeCaption"), "TextSecondaryBrush");
        _detailDuration = Text("", Ui.D("TypeCaption"), "TextSecondaryBrush");
        times.Children.Add(_detailTime);
        Put(times, _detailDuration, 0, 1);
        seekGroup.Children.Add(times);
        Put(layout, seekGroup, 4, 0);

        var controls = new Grid { ColumnDefinitions = new("*,Auto,Auto,Auto,*"),
            ColumnSpacing = Ui.D("Space4"), MinHeight = Ui.D("PlayerPlaySize") };

        Button? shuffle = null;
        shuffle = IconButton("shuffle", "Aleatório", () =>
        {
            _music.Shuffle = !_music.Shuffle;
            shuffle!.Content = ModeIndicator("shuffle", _music.Shuffle);
            return Task.CompletedTask;
        }, _music.Shuffle ? "AccentBrush" : "TextSecondaryBrush");
        shuffle.Content = ModeIndicator("shuffle", _music.Shuffle);
        shuffle.HorizontalAlignment = HorizontalAlignment.Center;
        controls.Children.Add(shuffle);

        var previous = IconButton("previous", "Anterior", _music.PreviousAsync,
            "TextPrimaryBrush");
        previous.Content = Icons.Make("previous", "TextPrimaryBrush", Ui.D("PlayerIconSize"));
        Put(controls, previous, 0, 1);

        _detailPlay = IconButton("play", "Reproduzir ou pausar", _music.ToggleAsync, "OnAccentBrush");
        _detailPlay.Classes.Remove("subtle"); _detailPlay.Classes.Add("primary");
        _detailPlay.Width = _detailPlay.Height = Ui.D("PlayerPlaySize");
        _detailPlay.CornerRadius = Ui.Radius("RadiusPill");
        Put(controls, _detailPlay, 0, 2);

        var next = IconButton("next", "Próxima", _music.NextAsync, "TextPrimaryBrush");
        next.Content = Icons.Make("next", "TextPrimaryBrush", Ui.D("PlayerIconSize"));
        Put(controls, next, 0, 3);

        Button? repeat = null;
        repeat = IconButton("repeat", "Repetir faixa", () =>
        {
            _music.Repeat = !_music.Repeat;
            repeat!.Content = RepeatIndicator(_music.Repeat);
            return Task.CompletedTask;
        }, _music.Repeat ? "AccentBrush" : "TextSecondaryBrush");
        repeat.Content = RepeatIndicator(_music.Repeat);
        repeat.HorizontalAlignment = HorizontalAlignment.Center;
        Put(controls, repeat, 0, 4);
        Put(layout, controls, 5, 0);

        var upcoming = _music.Queue.FirstOrDefault(candidate => candidate.Id != track.Id);
        var nextLabel = Text(upcoming is null
            ? "Fim da fila" : "Próxima: " + TrackTextFormatter.Title(upcoming.Title, upcoming.Artist),
            Ui.D("TypeCaption"), "TextSecondaryBrush");
        nextLabel.TextTrimming = TextTrimming.CharacterEllipsis; nextLabel.MaxLines = 1;
        nextLabel.HorizontalAlignment = HorizontalAlignment.Stretch;
        nextLabel.VerticalAlignment = VerticalAlignment.Bottom;
        Put(layout, nextLabel, 7, 0);

        var scroll = new ScrollViewer { Content = layout,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            Padding = Ui.Insets("PagePadding", "Space8", "PagePadding", "Space24") };
        var gradient = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            [
                new GradientStop(Ui.Color("PlayerGradientTopBrush"), 0),
                new GradientStop(Ui.Color("PlayerGradientBottomBrush"), 1)
            ]
        };
        artView.DominantColorReady += color =>
        {
            if (!ReferenceEquals(_detailTrack, track) || _playerPage is null) return;
            var dark = Artwork.Darkened(color);
            gradient.GradientStops[0].Color = dark;
            TopLevel.SetSystemBarColor(this, new SolidColorBrush(dark));
        };
        _playerPage = new Border { Background = gradient, Child = scroll };
        Grid.SetColumn(_playerPage, 1); Grid.SetRowSpan(_playerPage, 3);
        _root.Children.Add(_playerPage);
        TopLevel.SetSystemBarColor(this, (SolidColorBrush)Ui.B("PlayerGradientTopBrush"));
        _main.IsVisible = false; _mini.IsVisible = false; _bottom.IsVisible = false;
        Animate(_playerPage); RefreshPlayback();
    }

    private static Control ModeIndicator(string glyph, bool enabled)
    {
        var host = new Grid { Width = Ui.D("TouchTarget"), Height = Ui.D("TouchTarget") };
        host.Children.Add(Icons.Make(glyph, enabled ? "AccentBrush" : "TextSecondaryBrush",
            Ui.D("IconSize")));
        if (enabled)
        {
            var marker = new Border { Width = Ui.D("Space4"), Height = Ui.D("Space4"),
                CornerRadius = Ui.Radius("RadiusPill"), Background = Ui.B("AccentBrush"),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom };
            host.Children.Add(marker);
        }
        return host;
    }

    private static Control RepeatIndicator(bool enabled)
    {
        var host = new Grid { Width = Ui.D("TouchTarget"), Height = Ui.D("TouchTarget") };
        host.Children.Add(Icons.Make(enabled ? "repeat-one" : "repeat",
            enabled ? "AccentBrush" : "TextSecondaryBrush", Ui.D("IconSize")));
        if (enabled)
        {
            var marker = new Border { Width = Ui.D("Space4"), Height = Ui.D("Space4"),
                CornerRadius = Ui.Radius("RadiusPill"), Background = Ui.B("AccentBrush"),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom };
            host.Children.Add(marker);
        }
        return host;
    }
    private void ClosePlayer()
    {
        if (_playerPage is not null) _root.Children.Remove(_playerPage);
        _playerPage = null; _showingPlayer = false; _listFingerprint = null;
        TopLevel.SetSystemBarColor(this, (SolidColorBrush)Ui.B("BackgroundBrush"));
        _main.IsVisible = true; _bottom.IsVisible = Bounds.Width < Ui.D("DesktopBreakpoint");
        _detailTrack = null; _detailSeek = null; _detailPlay = null; _detailTime = null; _detailDuration = null; _seeking = false; Refresh(); Animate(_main);
    }
    private bool HandleBack()
    {
        if (_overlay is not null) { CloseSheet(); return true; }
        if (_showingPlayer) { ClosePlayer(); return true; }
        return false;
    }
    private static void Animate(Control control)
    {
        control.Transitions = null; control.Opacity = 0;
        control.Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(160) } };
        Dispatcher.UIThread.Post(() => control.Opacity = 1, DispatcherPriority.Render);
    }
    private void RefreshPlayback()
    {
        _equalizerFrame++;
        foreach (var (id, row) in _rows)
            UpdateRowState(id, row, _music.Pending ?? _music.Current);
        var icon = _music.LoadingAudio ? "close" : _music.Player.IsPlaying ? "pause" : "play";
        _play.Content = Icons.Make(icon, "OnAccentBrush", Ui.D("IconSize"));
        if (_detailPlay is not null)
            _detailPlay.Content = Icons.Make(icon, "OnAccentBrush", Ui.D("PlayerIconSize"));
        // Prefer the same metadata duration shown in lists; fallback for imported local audio.
        var duration = _music.Current?.DurationSeconds is > 0
            ? _music.Current.DurationSeconds : _music.Player.Duration;
        _progress.Maximum = Math.Max(1, duration); _progress.Value = Math.Min(_music.Player.Position, _progress.Maximum);
        if (_detailSeek is not null)
        {
            _detailSeek.Maximum = Math.Max(1, duration); _detailSeek.IsEnabled = duration > 0 && !_music.LoadingAudio;
            if (!_seeking) _detailSeek.Value = Math.Min(_music.Player.Position, _detailSeek.Maximum);
        }
        if (_detailTime is not null) _detailTime.Text = TrackTextFormatter.Duration(_music.Player.Position);
        if (_detailDuration is not null) _detailDuration.Text =
            TrackTextFormatter.Duration(duration, unknownAsDash: duration <= 0);
    }
    private void ShowSettings()
    {
        var panel = new StackPanel { Spacing = 12 };
        var session = System.IO.File.Exists(CookiesPath);
        var section = Text("YouTube", 15); section.FontWeight = FontWeight.SemiBold;
        panel.Children.Add(section);
        var note = Text(session ? "Sessão importada neste aparelho." : "Sessão não importada. Faixas públicas podem tocar sem login; o YouTube pode pedir verificação.", 12, Muted); note.TextWrapping = TextWrapping.Wrap; panel.Children.Add(note);
        panel.Children.Add(ActionButton("Importar cookies de sessão", ImportCookiesAsync));
        if (session) panel.Children.Add(ActionButton("Remover sessão", () => { System.IO.File.Delete(CookiesPath); AppServices.InvalidateCatalog(); ShowSettings(); return Task.CompletedTask; }));
        panel.Children.Add(ActionButton("Diagnóstico de reprodução", () => { ShowDiagnostics(); return Task.CompletedTask; }));
        ShowSheet("Configurações", panel);
    }
    private string CookiesPath => System.IO.Path.Combine(_music.DataDirectory, "youtube-cookies.txt");
    private async Task ImportCookiesAsync()
    {
        var top = TopLevel.GetTopLevel(this); if (top is null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Cookies no formato Netscape", AllowMultiple = false });
        if (files.Count == 0) return;
        using var file = files[0]; await using var input = await file.OpenReadAsync();
        using var reader = new System.IO.StreamReader(input);
        var buffer = new char[1_000_001]; var length = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
        if (length > 1_000_000) throw new IOException("O arquivo de cookies é muito grande.");
        var text = new string(buffer, 0, length);
        if (!text.StartsWith("# Netscape HTTP Cookie File") && !text.StartsWith("# HTTP Cookie File")) throw new IOException("Selecione um arquivo de cookies no formato Netscape.");
        System.IO.Directory.CreateDirectory(_music.DataDirectory);
        await System.IO.File.WriteAllTextAsync(CookiesPath, text);
        AppServices.InvalidateCatalog();
        _music.SetStatus("Sessão importada neste aparelho."); ShowSettings();
    }
    private void ShowDiagnostics()
    {
        var panel = new StackPanel { Spacing = 14 };
        var text = PlaybackDiagnostics.Snapshot(); if (string.IsNullOrWhiteSpace(text)) text = "Nenhuma tentativa registrada. Tente reproduzir uma faixa primeiro.";
        panel.Children.Add(new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = Ui.B(Muted) });
        panel.Children.Add(ActionButton("Copiar diagnóstico", async () => { var clipboard = TopLevel.GetTopLevel(this)?.Clipboard; if (clipboard is not null) await clipboard.SetTextAsync(PlaybackDiagnostics.Snapshot()); }));
        ShowSheet("Diagnóstico", panel);
    }
    private void ShowSheet(string title, Control content)
    {
        if (_overlay is not null) _root.Children.Remove(_overlay);
        var layout = new Grid { RowDefinitions = new("Auto,*"), RowSpacing = 18 };
        var header = new Grid { ColumnDefinitions = new("*,Auto"), MinHeight = 44 };
        var label = Text(title, 17); label.FontWeight = FontWeight.SemiBold;
        label.VerticalAlignment = VerticalAlignment.Center; header.Children.Add(label);
        Put(header, IconButton("close", "Fechar painel", () => { CloseSheet(); return Task.CompletedTask; }), 0, 1); layout.Children.Add(header);
        Put(layout, new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, 1, 0);
        var sheet = new Border { Width = Math.Max(0, Math.Min(400, Bounds.Width - 40)),
            MaxHeight = Math.Max(0, Bounds.Height - 48), Padding = new Thickness(20),
            CornerRadius = new CornerRadius(20), Background = Ui.B("SurfaceBrush"),
            BorderThickness = new Thickness(1), BorderBrush = Ui.B("BorderBrush"),
            Child = layout, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center };
        _overlay = new Border { Background = Ui.B("ScrimBrush"), Child = sheet };
        Grid.SetColumnSpan(_overlay, 2); Grid.SetRowSpan(_overlay, 3); _root.Children.Add(_overlay); Animate(sheet);
    }
    private void CloseSheet()
    {
        if (_overlay is not null) _root.Children.Remove(_overlay); _overlay = null;
        _detailTrack = null; _detailSeek = null; _detailPlay = null; _detailTime = null; _detailDuration = null; _seeking = false;
    }
    private static TextBlock Text(string text, double size, string color = "TextPrimaryBrush") =>
        new() { Text = text, FontSize = Math.Max(Ui.D("TypeCaption"), size), Foreground = Ui.B(color) };
    private Button ActionButton(string text, Func<Task> action)
    {
        var button = new Button { Content = text, HorizontalAlignment = HorizontalAlignment.Stretch };
        button.Click += async (_, _) => { try { await action(); } catch (Exception ex) { PlaybackDiagnostics.Write("Interface", ex.Message); _music.SetStatus(ex.Message); ShowDiagnostics(); } }; return button;
    }
    private Button IconButton(string icon, string label, Func<Task> action, string color = Muted)
    {
        var button = ActionButton("", action);
        button.Content = Icons.Make(icon, color.StartsWith("#", StringComparison.Ordinal)
            ? "TextSecondaryBrush" : color, Ui.D("IconSize"));
        button.Classes.Add("subtle"); button.Padding = Ui.Inset("Space8");
        button.Width = button.Height = Ui.D("TouchTarget");
        button.VerticalAlignment = VerticalAlignment.Center;
        Avalonia.Automation.AutomationProperties.SetName(button, label); ToolTip.SetTip(button, label); return button;
    }
    private static void Put(Grid grid, Control control, int row, int column) { Grid.SetRow(control, row); Grid.SetColumn(control, column); grid.Children.Add(control); }
}
