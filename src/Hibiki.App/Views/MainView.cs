using Avalonia;
using Avalonia.Animation;
using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Input.TextInput;
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
    private readonly TextBlock _count = Text("", 12, Ui.Brush("TextTertiary"));
    private readonly ListBox _tracks = new();
    private readonly ContentControl _emptyState = new();
    private readonly TextBlock _status = Text("", 12, Muted);
    private readonly Border _statusBox;
    private readonly Button _cancelSearch;
    private readonly Border _mini;
    private readonly ContentControl _nowArt = new();
    private readonly TextBlock _nowTitle = Text("", 13);
    private readonly TextBlock _nowArtist = Text("", 11, Muted);
    private readonly Button _play;
    private readonly ProgressBar _progress = new() { Height = Ui.Size("ProgressHeight"), Minimum = 0, Maximum = 1, Foreground = Accent, Background = Ui.Brush("TrackRail") };
    private readonly Grid _bottom;
    private readonly List<Button> _nav = [];
    private Border? _overlay;
    private Border? _playerPage;
    private Grid? _playerColumn;
    private Artwork? _playerArt;
    private TextBlock? _detailNext;
    private readonly HashSet<RowVisual> _rows = [];
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
    private static IBrush Muted => Ui.Brush("TextSecondary");
    private static IBrush Accent => Ui.Brush("Accent");

    public MainView(MusicController music)
    {
        _music = music; Background = Ui.Brush("Background");
        TopLevel.SetAutoSafeAreaPadding(this, true);
        TopLevel.SetSystemBarColor(this, new SolidColorBrush(Ui.Color("BackgroundColor")));
        _sidebar = BuildSidebar(); _root.Children.Add(_sidebar); Grid.SetRowSpan(_sidebar, 3);
        _main = new Border { Padding = Ui.Insets("CompactPageInsets") }; Put(_root, _main, 0, 1);
        var content = new Grid { RowDefinitions = new("Auto,Auto,Auto,*,Auto"), RowSpacing = Ui.Size("Space12") }; _main.Child = content;
        // One balanced toolbar: the actions stay grouped, even on narrow phones.
        var header = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = Ui.Size("Space12"), MinHeight = 48, Margin = Ui.Insets("HeaderMargin") };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Ui.Size("Space8"), VerticalAlignment = VerticalAlignment.Center };
        brand.Children.Add(Icons.Make("wave", Muted, 22));
        var title = Text("Hibiki", 21); title.FontWeight = FontWeight.SemiBold;
        title.VerticalAlignment = VerticalAlignment.Center; brand.Children.Add(title);
        header.Children.Add(brand);
        var headerActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Ui.Size("Space4"), VerticalAlignment = VerticalAlignment.Center };
        headerActions.Children.Add(IconButton("folder", "Abrir áudio local", OpenLocalAsync));
        headerActions.Children.Add(IconButton("settings", "Configurações e diagnóstico", () => { ShowSettings(); return Task.CompletedTask; }));
        Put(header, headerActions, 0, 1);
        content.Children.Add(header);
        _search.VerticalContentAlignment = VerticalAlignment.Center;
        _search.InnerLeftContent = new Border { Margin = Ui.Insets("SearchIconMargin"), Child = Icons.Make("search", Muted, 24) };
        var clear = IconButton("close", "Limpar busca", () => { _search.Text = ""; return Task.CompletedTask; });
        clear.IsVisible = false; _search.InnerRightContent = clear;
        _search.TextChanged += (_, _) => clear.IsVisible = !string.IsNullOrEmpty(_search.Text);
        TextInputOptions.SetReturnKeyType(_search, TextInputReturnKeyType.Search);
        _search.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await SearchAsync(); } };
        Put(content, _search, 1, 0);
        var heading = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = Ui.Size("Space8"), Margin = Ui.Insets("HeadingMargin") };
        _heading.TextTrimming = TextTrimming.CharacterEllipsis; _heading.FontWeight = FontWeight.SemiBold;
        _count.VerticalAlignment = VerticalAlignment.Center; heading.Children.Add(_heading); Put(heading, _count, 0, 1); Put(content, heading, 2, 0);
        _tracks.Classes.Add("music-list");
        ScrollViewer.SetVerticalScrollBarVisibility(_tracks, ScrollBarVisibility.Hidden);
        ScrollViewer.SetHorizontalScrollBarVisibility(_tracks, ScrollBarVisibility.Disabled);
        // Avalonia can call the template with null while clearing a virtualized container.
        _tracks.ItemTemplate = new FuncDataTemplate<Track>((track, _) => track is null ? new Border() : TrackRow(track),
            supportsRecycling: false);
        var listArea = new Grid { ClipToBounds = true };
        listArea.Children.Add(_tracks); listArea.Children.Add(_emptyState);
        listArea.Children.Add(new Border { Height = Ui.Size("Space8"), VerticalAlignment = VerticalAlignment.Top,
            Background = Ui.Brush("ListFadeTop"), IsHitTestVisible = false });
        listArea.Children.Add(new Border { Height = Ui.Size("Space8"), VerticalAlignment = VerticalAlignment.Bottom,
            Background = Ui.Brush("ListFadeBottom"), IsHitTestVisible = false });
        Put(content, listArea, 3, 0);
        var statusRow = new Grid { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = Ui.Size("Space8") };
        _status.TextWrapping = TextWrapping.Wrap; _status.MaxLines = 2; _status.VerticalAlignment = VerticalAlignment.Center; statusRow.Children.Add(_status);
        _cancelSearch = IconButton("close", "Cancelar busca", () => { _music.CancelSearch(); return Task.CompletedTask; });
        Put(statusRow, _cancelSearch, 0, 1);
        Put(statusRow, IconButton("info", "Ver diagnóstico", () => { ShowDiagnostics(); return Task.CompletedTask; }), 0, 2);
        _statusBox = new Border { Child = statusRow, Padding = Ui.Insets("StatusInsets"), Background = Ui.Brush("Surface"), CornerRadius = Ui.Radius("CardRadius") }; Put(content, _statusBox, 4, 0);
        _play = IconButton("play", "Reproduzir, pausar ou cancelar", _music.ToggleAsync);
        _mini = BuildMiniPlayer(); Put(_root, _mini, 1, 1);
        _bottom = new Grid { ColumnDefinitions = new("*,*,*"), Height = Ui.Size("NavigationHeight"), Background = Ui.Brush("Surface") };
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
            _main.Padding = Ui.Insets(compact ? "CompactPageInsets" : "DesktopPageInsets");
            ResizePlayer();
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
        var panel = new StackPanel { Spacing = Ui.Size("Space8"), Margin = Ui.Insets("SidebarMargin") };
        var title = Text("Sua música", 18); title.Margin = Ui.Insets("SidebarHeadingMargin"); panel.Children.Add(title);
        panel.Children.Add(NavButton("explore", "Buscar", "search", false));
        panel.Children.Add(NavButton("library", "Biblioteca", "library", false));
        panel.Children.Add(NavButton("queue", "Fila", "queue", false));
        return new Border { Child = panel, Background = Ui.Brush("Surface") };
    }
    private Button NavButton(string key, string label, string icon, bool vertical)
    {
        var button = ActionButton("", () => { _page = key; Refresh(); Animate(_tracks); return Task.CompletedTask; });
        button.Classes.Add("nav"); button.Classes.Add(vertical ? "bottom-nav" : "side-nav");
        button.Tag = (key, label, icon, vertical); _nav.Add(button);
        button.Padding = Ui.Insets(vertical ? "NavInsets" : "SideNavInsets");
        button.HorizontalContentAlignment = vertical ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        return button;
    }
    private Border BuildMiniPlayer()
    {
        var grid = new Grid { RowDefinitions = new("Auto,Auto") };
        var row = new Grid { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = Ui.Size("Space4"), Margin = Ui.Insets("MiniContentInsets") };
        var open = ActionButton("", () => { ShowPlayer(); return Task.CompletedTask; });
        Avalonia.Automation.AutomationProperties.SetName(open, "Abrir player");
        open.Classes.Add("track"); open.Padding = Ui.Insets("NoThickness"); open.MinWidth = 0;
        open.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        var metadata = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = Ui.Size("Space8"), MinWidth = 0 };
        _nowArt.Content = new Artwork(null, 40); metadata.Children.Add(_nowArt);
        var info = new Grid { RowDefinitions = new("Auto,Auto"), RowSpacing = Ui.Size("Space4"), MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center };
        _nowTitle.TextTrimming = _nowArtist.TextTrimming = TextTrimming.CharacterEllipsis;
        _nowTitle.MaxLines = _nowArtist.MaxLines = 1;
        info.Children.Add(_nowTitle); Put(info, _nowArtist, 1, 0);
        Put(metadata, info, 0, 1); open.Content = metadata; row.Children.Add(open);
        Put(row, _play, 0, 1); Put(row, IconButton("next", "Próxima faixa", _music.NextAsync), 0, 2);
        grid.Children.Add(row);
        _progress.Margin = Ui.Insets("MiniProgressMargin");
        Put(grid, _progress, 1, 0);
        return new Border { Child = grid, Margin = Ui.Insets("MiniMargin"), CornerRadius = Ui.Radius("CardRadius"),
            ClipToBounds = true, Background = Ui.Brush("SurfaceElevated"), BoxShadow = Ui.Shadow("CardShadow") };
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
        _status.Text = _music.Status; _cancelSearch.IsVisible = _music.Searching;
        foreach (var button in _nav)
        {
            var (key, label, icon, vertical) = ((string, string, string, bool))button.Tag!;
            var active = key == _page;
            button.Classes.Set("active", active);
            var stack = new StackPanel { Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal,
                Spacing = Ui.Size(vertical ? "Space4" : "Space12"),
                HorizontalAlignment = vertical ? HorizontalAlignment.Center : HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center };
            var image = Icons.Make(icon, active ? Accent : Muted, 20);
            image.HorizontalAlignment = HorizontalAlignment.Center; image.VerticalAlignment = VerticalAlignment.Center;
            if (vertical)
                stack.Children.Add(new Border { Width = Ui.Size("NavigationPillWidth"), Height = Ui.Size("NavigationPillHeight"),
                    CornerRadius = Ui.Radius("PillRadius"), Background = active ? Ui.Brush("ActivePill") : Brushes.Transparent,
                    Child = image });
            else stack.Children.Add(image);
            var name = Text(label, vertical ? 12 : 13, active ? Accent : Muted);
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
        _tracks.ItemsSource = tracks.ToArray();
        _tracks.IsVisible = tracks.Count > 0;
        _emptyState.IsVisible = tracks.Count == 0;
        if (tracks.Count == 0)
        {
            var empty = new StackPanel { Spacing = Ui.Size("Space12"), Margin = Ui.Insets("EmptyStateMargin"),
                HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 320 };
            var icon = Icons.Make(_page == "library" ? "heart" : "wave", Muted, 32); icon.HorizontalAlignment = HorizontalAlignment.Center; empty.Children.Add(icon);
            var message = Text(_page switch { "library" => "Suas favoritas ficam aqui.", "queue" => "Adicione músicas à fila.", _ => "Busque uma música ou artista\npara começar a ouvir." }, 13, Muted);
            message.TextAlignment = TextAlignment.Center; empty.Children.Add(message);
            var import = ActionButton("Abrir áudio local", OpenLocalAsync);
            import.Width = 200; import.HorizontalAlignment = HorizontalAlignment.Center;
            import.Background = Ui.Brush("SurfaceElevated");
            empty.Children.Add(import); _emptyState.Content = empty;
        }
        else _emptyState.Content = null;
        }
        var displayed = _music.Pending ?? _music.Current;
        _mini.IsVisible = displayed is not null && !_showingPlayer;
        foreach (var row in _rows.ToArray()) UpdateRow(row, displayed);
        if (!Equals(displayed, _shownTrack)) { _shownTrack = displayed; _nowArt.Content = new Artwork(displayed, 40); }
        _nowTitle.Text = displayed is null ? "" : TrackTextFormatter.Title(displayed.Title, displayed.Artist);
        _nowArtist.Text = _music.LoadingAudio ? _music.Status : TrackTextFormatter.Artist(displayed?.Artist);
        if (_detailPlay is not null && displayed is not null && !Equals(_detailTrack, displayed)) ShowPlayer();
        UpdateNextLabel();
        RefreshPlayback();
    }
    private Control TrackRow(Track track)
    {
        // The ListBox gives the template a finite viewport and creates rows on demand.
        var grid = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = Ui.Size("Space4"),
            Height = Ui.Size("TrackRowHeight"), MinWidth = 0 };
        var play = ActionButton("", async () => await _music.PlayAsync(track));
        play.Classes.Add("track"); play.Padding = Ui.Insets("NoThickness");
        play.MinWidth = 0; play.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Avalonia.Automation.AutomationProperties.SetName(play, track.Title + " — " + track.Artist);

        var metadata = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = Ui.Size("Space8"), MinWidth = 0 };
        var artwork = new Grid { Width = Ui.Size("TrackThumbSize"), Height = Ui.Size("TrackThumbSize"),
            VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true };
        artwork.Children.Add(new Artwork(track, Ui.Size("TrackThumbSize")));
        var indicator = Icons.Make("wave", Accent, 20);
        indicator.HorizontalAlignment = HorizontalAlignment.Center; indicator.VerticalAlignment = VerticalAlignment.Center;
        indicator.Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(250) } };
        var shade = new Border { Background = Ui.Brush("Overlay"), Child = indicator, IsVisible = false,
            CornerRadius = Ui.Radius("ThumbRadius"), ClipToBounds = true };
        artwork.Children.Add(shade); metadata.Children.Add(artwork);
        var labels = new Grid { RowDefinitions = new("Auto,Auto"), RowSpacing = Ui.Size("Space4"), MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center };
        var title = Text(TrackTextFormatter.Title(track.Title, track.Artist), 13);
        title.TextTrimming = TextTrimming.CharacterEllipsis; title.MaxLines = 1;
        labels.Children.Add(title);
        var artist = Text(TrackTextFormatter.Artist(track.Artist), 12, Muted);
        artist.TextTrimming = TextTrimming.CharacterEllipsis; artist.MaxLines = 1;
        Put(labels, artist, 1, 0);
        Put(metadata, labels, 0, 1);
        var duration = Text(track.DurationLabel, 12, Ui.Brush("TextTertiary"));
        duration.VerticalAlignment = VerticalAlignment.Center; Put(metadata, duration, 0, 2);
        play.Content = metadata; grid.Children.Add(play);

        var more = IconButton("more", "Opções de " + track.Title, () => Task.CompletedTask);
        more.VerticalAlignment = VerticalAlignment.Center;
        var favorite = new MenuItem { Header = _music.IsFavorite(track) ? "Remover dos favoritos" : "Salvar nos favoritos" };
        favorite.Click += async (_, _) => await _music.ToggleFavoriteAsync(track);
        var queue = new MenuItem { Header = "Adicionar à fila" };
        queue.Click += (_, _) => _music.Enqueue(track);
        var menu = new ContextMenu { ItemsSource = new[] { favorite, queue } };
        more.Click += (_, _) => menu.Open(more);
        Put(grid, more, 0, 1);

        var border = new Border { Child = grid, CornerRadius = Ui.Radius("ThumbRadius"), ClipToBounds = true };
        var visual = new RowVisual(track, border, title, shade, indicator);
        border.AttachedToVisualTree += (_, _) => { _rows.Add(visual); UpdateRow(visual, _music.Pending ?? _music.Current); };
        border.DetachedFromVisualTree += (_, _) => _rows.Remove(visual);
        return border;
    }
    private sealed record RowVisual(Track Track, Border Border, TextBlock Title, Border Shade, PathIcon Indicator);

    private void UpdateRow(RowVisual row, Track? displayed)
    {
        var current = row.Track.Id == displayed?.Id;
        row.Border.Background = current ? Ui.Brush("SurfaceElevated") : Brushes.Transparent;
        row.Title.Foreground = current ? Accent : Ui.Brush("TextPrimary");
        row.Shade.IsVisible = current;
        row.Indicator.Opacity = current && _music.Player.IsPlaying && ((int)(_music.Player.Position * 2) % 2 == 0) ? 0.55 : 1;
    }
    private void ShowPlayer()
    {
        var track = _music.Pending ?? _music.Current; if (track is null) return;
        if (_overlay is not null) CloseSheet();
        _detailTrack = track; _showingPlayer = true;
        if (_playerPage is not null) _root.Children.Remove(_playerPage);

        var panel = new Grid { RowDefinitions = new("Auto,Auto,Auto,Auto,*,Auto,*,Auto"),
            Margin = Ui.Insets("PlayerColumnMargin"), HorizontalAlignment = HorizontalAlignment.Center };
        _playerColumn = panel;
        var header = new Grid { ColumnDefinitions = new("Auto,*,Auto") };
        header.Children.Add(IconButton("back", "Fechar player", () => { ClosePlayer(); return Task.CompletedTask; }));
        // The current Track model has no album field.
        var eyebrow = Text("Reproduzindo", 12, Muted);
        eyebrow.HorizontalAlignment = HorizontalAlignment.Center; eyebrow.VerticalAlignment = VerticalAlignment.Center;
        Put(header, eyebrow, 0, 1);
        Put(header, IconButton("queue", "Abrir fila", () => { ClosePlayer(); _page = "queue"; Refresh(); return Task.CompletedTask; }), 0, 2);
        panel.Children.Add(header);

        var page = new Border { Background = Ui.Brush("Background") };
        _playerPage = page;
        var art = new Artwork(track, Math.Max(1, Math.Min(Ui.Size("PlayerMaxWidth"), Bounds.Width - 2 * Ui.Size("PagePadding"))),
            color =>
            {
                if (!ReferenceEquals(_playerPage, page)) return;
                page.Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops = [new GradientStop(color, 0), new GradientStop(Ui.Color("BackgroundColor"), 1)]
                };
                TopLevel.SetSystemBarColor(this, new SolidColorBrush(color));
            });
        _playerArt = art;
        art.Margin = Ui.Insets("PlayerArtMargin"); art.BoxShadow = Ui.Shadow("CardShadow");
        Put(panel, art, 1, 0);

        var metadata = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = Ui.Size("Space8"),
            Margin = Ui.Insets("PlayerMetadataMargin"), MinWidth = 0 };
        var labels = new StackPanel { Spacing = Ui.Size("Space4"), MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
        var title = Text(TrackTextFormatter.Title(track.Title, track.Artist), 22); title.FontWeight = FontWeight.Bold;
        title.TextWrapping = TextWrapping.Wrap; title.MaxLines = 2; labels.Children.Add(title);
        var artist = Text(TrackTextFormatter.Artist(track.Artist), 14, Muted);
        artist.TextTrimming = TextTrimming.CharacterEllipsis; artist.MaxLines = 1; labels.Children.Add(artist);
        metadata.Children.Add(labels);
        Button? favorite = null;
        favorite = IconButton(_music.IsFavorite(track) ? "heart_filled" : "heart", "Alternar favorito", async () =>
        {
            await _music.ToggleFavoriteAsync(track);
            var active = _music.IsFavorite(track);
            favorite!.Content = Icons.Make(active ? "heart_filled" : "heart", active ? Accent : Muted, 24);
            favorite.Opacity = 0.55;
            Dispatcher.UIThread.Post(() => favorite.Opacity = 1, DispatcherPriority.Render);
        }, _music.IsFavorite(track) ? Accent : Muted);
        favorite.Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(160) } };
        Put(metadata, favorite, 0, 1); Put(panel, metadata, 2, 0);

        var seekGroup = new StackPanel();
        _detailSeek = new Slider { Minimum = 0, Maximum = 1, Theme = Ui.Theme("PlayerSlider") };
        _detailSeek.AddHandler(PointerPressedEvent, (_, _) => _seeking = true, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _detailSeek.AddHandler(PointerReleasedEvent, (_, _) => { _seeking = false; _music.Player.Seek(_detailSeek.Value); }, Avalonia.Interactivity.RoutingStrategies.Bubble, true);
        _detailSeek.KeyUp += (_, _) => _music.Player.Seek(_detailSeek.Value);
        seekGroup.Children.Add(_detailSeek);
        var times = new Grid { ColumnDefinitions = new("*,Auto") };
        _detailTime = Text("", 12, Muted); _detailDuration = Text("", 12, Muted);
        times.Children.Add(_detailTime); Put(times, _detailDuration, 0, 1);
        seekGroup.Children.Add(times); Put(panel, seekGroup, 3, 0);

        var controls = new Grid { ColumnDefinitions = new("*,*,Auto,*,*"), Margin = Ui.Insets("PlayerControlMargin") };
        Button? shuffle = null;
        shuffle = IconButton("shuffle", "Aleatório", () =>
        {
            _music.Shuffle = !_music.Shuffle;
            shuffle!.Content = ModeIcon("shuffle", _music.Shuffle);
            UpdateNextLabel(); return Task.CompletedTask;
        });
        shuffle.Content = ModeIcon("shuffle", _music.Shuffle); shuffle.HorizontalAlignment = HorizontalAlignment.Center; controls.Children.Add(shuffle);
        var previous = IconButton("previous", "Anterior", _music.PreviousAsync, Ui.Brush("TextPrimary"));
        previous.Content = Icons.Make("previous", Ui.Brush("TextPrimary"), Ui.Size("PlayerSkipIconSize"));
        previous.HorizontalAlignment = HorizontalAlignment.Center; Put(controls, previous, 0, 1);
        _detailPlay = IconButton("play", "Reproduzir ou pausar", _music.ToggleAsync);
        _detailPlay.Classes.Remove("subtle"); _detailPlay.Classes.Add("primary");
        _detailPlay.Width = _detailPlay.Height = Ui.Size("PlayerPlaySize"); _detailPlay.CornerRadius = Ui.Radius("PillRadius");
        Put(controls, _detailPlay, 0, 2);
        var next = IconButton("next", "Próxima", _music.NextAsync, Ui.Brush("TextPrimary"));
        next.Content = Icons.Make("next", Ui.Brush("TextPrimary"), Ui.Size("PlayerSkipIconSize"));
        next.HorizontalAlignment = HorizontalAlignment.Center; Put(controls, next, 0, 3);
        Button? repeat = null;
        repeat = IconButton("repeat", "Repetir faixa", () =>
        {
            _music.Repeat = !_music.Repeat;
            repeat!.Content = ModeIcon("repeat", _music.Repeat);
            UpdateNextLabel(); return Task.CompletedTask;
        });
        repeat.Content = ModeIcon("repeat", _music.Repeat); repeat.HorizontalAlignment = HorizontalAlignment.Center;
        Put(controls, repeat, 0, 4); Put(panel, controls, 5, 0);

        _detailNext = Text("", 12, Muted); _detailNext.Margin = Ui.Insets("PlayerNextMargin");
        _detailNext.TextTrimming = TextTrimming.CharacterEllipsis; _detailNext.MaxLines = 1;
        Put(panel, _detailNext, 7, 0); UpdateNextLabel();
        var scroll = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
        page.Child = scroll;
        Grid.SetColumn(page, 1); Grid.SetRowSpan(page, 3); _root.Children.Add(page);
        ResizePlayer();
        _main.IsVisible = false; _mini.IsVisible = false; _bottom.IsVisible = false;
        Animate(page); RefreshPlayback();
    }
    private static Control ModeIcon(string icon, bool active)
    {
        var grid = new Grid { Width = Ui.Size("TouchTarget"), Height = Ui.Size("TouchTarget") };
        var image = Icons.Make(icon == "repeat" && active ? "repeat_one" : icon, active ? Accent : Muted, 24);
        image.HorizontalAlignment = HorizontalAlignment.Center; image.VerticalAlignment = VerticalAlignment.Center; grid.Children.Add(image);
        if (active) grid.Children.Add(new Border { Width = Ui.Size("Space4"), Height = Ui.Size("Space4"),
            Background = Accent, CornerRadius = Ui.Radius("PillRadius"), VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Center });
        return grid;
    }
    private void ResizePlayer()
    {
        if (_playerColumn is null || _playerArt is null) return;
        var sidebar = Bounds.Width < 800 ? 0 : 200;
        var width = Math.Max(0, Math.Min(Ui.Size("PlayerMaxWidth"), Bounds.Width - sidebar - 2 * Ui.Size("PagePadding")));
        _playerColumn.Width = width;
        _playerColumn.MinHeight = Math.Max(0, Bounds.Height - Ui.Size("Space24"));
        _playerArt.Width = _playerArt.Height = width;
        _playerArt.CornerRadius = Ui.Radius("CoverRadius");
    }
    private void UpdateNextLabel()
    {
        if (_detailNext is null || _detailTrack is null) return;
        if (_music.Repeat) { _detailNext.Text = "Próxima: " + TrackTextFormatter.Title(_detailTrack.Title, _detailTrack.Artist); return; }
        if (_music.Shuffle) { _detailNext.Text = "Próxima: aleatória"; return; }
        var matches = _music.Queue.Select((item, index) => (item, index)).Where(x => x.item.Id == _detailTrack.Id).ToArray();
        if (matches.Length != 1) { _detailNext.Text = "Próxima: —"; return; }
        var nextIndex = matches[0].index + 1;
        var next = nextIndex < _music.Queue.Count ? _music.Queue[nextIndex] : null;
        _detailNext.Text = next is null ? "Próxima: —" : "Próxima: " + TrackTextFormatter.Title(next.Title, next.Artist);
    }
    private void ClosePlayer()
    {
        if (_playerPage is not null) _root.Children.Remove(_playerPage);
        _playerPage = null; _showingPlayer = false; _listFingerprint = null; _main.IsVisible = true; _bottom.IsVisible = Bounds.Width < 800;
        _playerColumn = null; _playerArt = null; _detailNext = null;
        TopLevel.SetSystemBarColor(this, new SolidColorBrush(Ui.Color("BackgroundColor")));
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
        foreach (var row in _rows.ToArray()) UpdateRow(row, _music.Pending ?? _music.Current);
        var icon = _music.LoadingAudio ? "close" : _music.Player.IsPlaying ? "pause" : "play";
        _play.Content = new Border { Width = Ui.Size("MiniPlayVisualSize"), Height = Ui.Size("MiniPlayVisualSize"),
            CornerRadius = Ui.Radius("PillRadius"), Background = Accent,
            Child = Icons.Make(icon, Ui.Brush("OnAccent"), 20) };
        if (_detailPlay is not null) _detailPlay.Content = Icons.Make(icon, Ui.Brush("OnAccent"), Ui.Size("PlayerSkipIconSize"));
        var duration = _music.Player.Duration; if (duration <= 0) duration = _music.Current?.DurationSeconds ?? 0;
        _progress.Maximum = Math.Max(1, duration); _progress.Value = Math.Min(_music.Player.Position, _progress.Maximum);
        if (_detailSeek is not null)
        {
            _detailSeek.Maximum = Math.Max(1, duration); _detailSeek.IsEnabled = duration > 0 && !_music.LoadingAudio;
            if (!_seeking) _detailSeek.Value = Math.Min(_music.Player.Position, _detailSeek.Maximum);
        }
        if (_detailTime is not null) _detailTime.Text = TrackTextFormatter.Duration(_music.Player.Position);
        // Prefer the same metadata used by the list; the player can report a
        // slightly different stream length after decoding starts.
        var displayDuration = (_detailTrack?.DurationSeconds ?? 0) > 0
            ? _detailTrack!.DurationSeconds : duration;
        if (_detailDuration is not null) _detailDuration.Text = TrackTextFormatter.Duration(displayDuration, unknownWhenZero: true);
    }
    private void ShowSettings()
    {
        var panel = new StackPanel { Spacing = Ui.Size("Space12") };
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
        var panel = new StackPanel { Spacing = Ui.Size("Space12") };
        var text = PlaybackDiagnostics.Snapshot(); if (string.IsNullOrWhiteSpace(text)) text = "Nenhuma tentativa registrada. Tente reproduzir uma faixa primeiro.";
        panel.Children.Add(new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Muted });
        panel.Children.Add(ActionButton("Copiar diagnóstico", async () => { var clipboard = TopLevel.GetTopLevel(this)?.Clipboard; if (clipboard is not null) await clipboard.SetTextAsync(PlaybackDiagnostics.Snapshot()); }));
        ShowSheet("Diagnóstico", panel);
    }
    private void ShowSheet(string title, Control content)
    {
        if (_overlay is not null) _root.Children.Remove(_overlay);
        var layout = new Grid { RowDefinitions = new("Auto,*"), RowSpacing = Ui.Size("Space16") };
        var header = new Grid { ColumnDefinitions = new("*,Auto"), MinHeight = 44 };
        var label = Text(title, 17); label.FontWeight = FontWeight.SemiBold;
        label.VerticalAlignment = VerticalAlignment.Center; header.Children.Add(label);
        Put(header, IconButton("close", "Fechar painel", () => { CloseSheet(); return Task.CompletedTask; }), 0, 1); layout.Children.Add(header);
        Put(layout, new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, 1, 0);
        var sheet = new Border { Width = Math.Max(0, Math.Min(400, Bounds.Width - 40)),
            MaxHeight = Math.Max(0, Bounds.Height - 48), Padding = Ui.Insets("SheetInsets"),
            CornerRadius = Ui.Radius("CardRadius"), Background = Ui.Brush("SurfaceElevated"),
            BorderThickness = new Thickness(1), BorderBrush = Ui.Brush("Outline"),
            Child = layout, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center };
        _overlay = new Border { Background = Ui.Brush("Overlay"), Child = sheet };
        Grid.SetColumnSpan(_overlay, 2); Grid.SetRowSpan(_overlay, 3); _root.Children.Add(_overlay); Animate(sheet);
    }
    private void CloseSheet()
    {
        if (_overlay is not null) _root.Children.Remove(_overlay); _overlay = null;
        _detailTrack = null; _detailSeek = null; _detailPlay = null; _detailTime = null; _detailDuration = null; _seeking = false;
    }
    private static TextBlock Text(string text, double size, IBrush? color = null) => new() { Text = text, FontSize = Math.Max(12, size), Foreground = color ?? Ui.Brush("TextPrimary") };
    private Button ActionButton(string text, Func<Task> action)
    {
        var button = new Button { Content = text, HorizontalAlignment = HorizontalAlignment.Stretch };
        button.Click += async (_, _) => { try { await action(); } catch (Exception ex) { PlaybackDiagnostics.Write("Interface", ex.Message); _music.SetStatus(ex.Message); ShowDiagnostics(); } }; return button;
    }
    private Button IconButton(string icon, string label, Func<Task> action, IBrush? color = null)
    {
        var button = ActionButton("", action); button.Content = Icons.Make(icon, color ?? Muted, 24); button.Classes.Add("subtle"); button.Padding = Ui.Insets("NoThickness"); button.Width = button.Height = Ui.Size("TouchTarget"); button.VerticalAlignment = VerticalAlignment.Center;
        Avalonia.Automation.AutomationProperties.SetName(button, label); ToolTip.SetTip(button, label); return button;
    }
    private static void Put(Grid grid, Control control, int row, int column) { Grid.SetRow(control, row); Grid.SetColumn(control, column); grid.Children.Add(control); }
}
