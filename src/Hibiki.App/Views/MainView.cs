using Avalonia;
using Avalonia.Animation;
using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
    private readonly TextBox _search = new() { PlaceholderText = "Música, artista ou link", MinWidth = 0, Height = 44, MinHeight = 44, FontSize = 14 };
    private readonly TextBlock _heading = Text("Buscar músicas", 16);
    private readonly TextBlock _count = Text("", 11, Muted);
    private readonly StackPanel _tracks = new() { Spacing = 0 };
    private readonly TextBlock _status = Text("", 12, Muted);
    private readonly Border _statusBox;
    private readonly Button _searchButton;
    private readonly Button _cancelSearch;
    private readonly Border _mini;
    private readonly ContentControl _nowArt = new();
    private readonly TextBlock _nowTitle = Text("", 13);
    private readonly TextBlock _nowArtist = Text("", 11, Muted);
    private readonly Button _play;
    private readonly ProgressBar _progress = new() { Height = 2, Minimum = 0, Maximum = 1, Foreground = Brush.Parse(Accent), Background = Brush.Parse("#26232E") };
    private readonly Grid _bottom;
    private readonly List<Button> _nav = [];
    private Border? _overlay;
    private Border? _playerPage;
    private readonly Dictionary<string, Border> _rows = new();
    private string? _listFingerprint;
    private bool _showingPlayer;
    private Slider? _detailSeek;
    private TextBlock? _detailTime;
    private Button? _detailPlay;
    private bool _seeking;
    private Track? _shownTrack;
    private Track? _detailTrack;
    private string _page = "explore";
    private const string Muted = "#9992A6", Accent = "#BDA2FF";

    public MainView(MusicController music)
    {
        _music = music; Background = Brush.Parse("#111015");
        _sidebar = BuildSidebar(); _root.Children.Add(_sidebar); Grid.SetRowSpan(_sidebar, 3);
        _main = new Border { Padding = new Thickness(18, 12, 18, 4) }; Put(_root, _main, 0, 1);
        var content = new Grid { RowDefinitions = new("Auto,Auto,Auto,*,Auto"), RowSpacing = 12 }; _main.Child = content;
        var header = new Grid { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = 4, Margin = new Thickness(0, 0, 0, 2) };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9, VerticalAlignment = VerticalAlignment.Center };
        brand.Children.Add(Icons.Make("wave", Accent, 22));
        var title = Text("Hibiki", 21); title.FontWeight = FontWeight.SemiBold; brand.Children.Add(title);
        var by = Text("by Flugel", 10, Muted); by.VerticalAlignment = VerticalAlignment.Center; header.Children.Add(brand);
        Put(header, IconButton("folder", "Abrir áudio local", OpenLocalAsync), 0, 1);
        Put(header, IconButton("settings", "Configurações e diagnóstico", () => { ShowSettings(); return Task.CompletedTask; }), 0, 2);
        content.Children.Add(header);
        var searchRow = new Grid { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = 8 };
        searchRow.Children.Add(_search);
        _searchButton = IconButton("search", "Buscar", SearchAsync); _searchButton.Classes.Remove("subtle"); _searchButton.Classes.Add("primary"); _searchButton.Content = Icons.Make("search", "#211337", 19);
        Put(searchRow, _searchButton, 0, 1);
        _cancelSearch = IconButton("close", "Cancelar busca", () => { _music.CancelSearch(); return Task.CompletedTask; }); Put(searchRow, _cancelSearch, 0, 2);
        _search.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await SearchAsync(); } };
        Put(content, searchRow, 1, 0);
        var heading = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        _heading.TextTrimming = TextTrimming.CharacterEllipsis; _heading.FontWeight = FontWeight.SemiBold;
        _count.VerticalAlignment = VerticalAlignment.Center; heading.Children.Add(_heading); Put(heading, _count, 0, 1); Put(content, heading, 2, 0);
        Put(content, new ScrollViewer { Content = _tracks, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden }, 3, 0);
        var statusRow = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 };
        _status.TextWrapping = TextWrapping.Wrap; _status.MaxLines = 2; _status.VerticalAlignment = VerticalAlignment.Center; statusRow.Children.Add(_status);
        Put(statusRow, IconButton("info", "Ver diagnóstico", () => { ShowDiagnostics(); return Task.CompletedTask; }), 0, 1);
        _statusBox = new Border { Child = statusRow, Padding = new Thickness(10, 2), Background = Brush.Parse("#1E1927"), CornerRadius = new CornerRadius(10) }; Put(content, _statusBox, 4, 0);
        _play = IconButton("play", "Reproduzir, pausar ou cancelar", _music.ToggleAsync);
        _mini = BuildMiniPlayer(); Put(_root, _mini, 1, 1);
        _bottom = new Grid { ColumnDefinitions = new("*,*,*"), Height = 60, Background = Brush.Parse("#15131B") };
        var tabs = new[] { ("explore", "Buscar", "search"), ("library", "Biblioteca", "heart"), ("queue", "Fila", "queue") };
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
        _music.Changed += Refresh; _music.Tick += RefreshPlayback;
        AppServices.BackHandler = HandleBack; AppServices.OpenPlayerView = ShowPlayer;
        AttachedToVisualTree += async (_, _) => { _music.StartObserving(); await _music.InitializeAsync(); };
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
        return new Border { Child = panel, Background = Brush.Parse("#15131B") };
    }
    private Button NavButton(string key, string label, string icon, bool vertical)
    {
        var button = ActionButton("", () => { _page = key; Refresh(); Animate(_tracks); return Task.CompletedTask; });
        button.Classes.Add("nav"); button.Tag = (key, label, icon, vertical); _nav.Add(button);
        button.Padding = new Thickness(8, 5); button.HorizontalContentAlignment = vertical ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        return button;
    }
    private Border BuildMiniPlayer()
    {
        var grid = new Grid { RowDefinitions = new("Auto,Auto") };
        var row = new Grid { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = 4, Margin = new Thickness(12, 7) };
        var open = ActionButton("", () => { ShowPlayer(); return Task.CompletedTask; }); Avalonia.Automation.AutomationProperties.SetName(open, "Abrir player"); open.Classes.Add("subtle"); open.Padding = new Thickness(0); open.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        var metadata = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 10 };
        _nowArt.Content = new Artwork(null, 40); metadata.Children.Add(_nowArt);
        var info = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        _nowTitle.TextTrimming = _nowArtist.TextTrimming = TextTrimming.CharacterEllipsis; info.Children.Add(_nowTitle); info.Children.Add(_nowArtist); Put(metadata, info, 0, 1);
        open.Content = metadata; row.Children.Add(open); Put(row, _play, 0, 1); Put(row, IconButton("next", "Próxima faixa", _music.NextAsync), 0, 2);
        grid.Children.Add(row); Put(grid, _progress, 1, 0);
        return new Border { Child = grid, Margin = new Thickness(12, 4, 12, 6), CornerRadius = new CornerRadius(14), ClipToBounds = true, Background = Brush.Parse("#211B2B"), BorderThickness = new Thickness(0, 1, 0, 0), BorderBrush = Brush.Parse("#32263F") };
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
        _status.Text = _music.Status; _cancelSearch.IsVisible = _music.Searching; _searchButton.IsEnabled = !_music.Searching;
        foreach (var button in _nav)
        {
            var (key, label, icon, vertical) = ((string, string, string, bool))button.Tag!;
            var active = key == _page;
            button.Classes.Set("active", active && !vertical);
            if (vertical) { button.BorderThickness = new Thickness(0, 0, 0, active ? 2 : 0); button.BorderBrush = Brush.Parse(Accent); button.CornerRadius = new CornerRadius(0); }
            var stack = new StackPanel { Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal, Spacing = vertical ? 3 : 12, HorizontalAlignment = vertical ? HorizontalAlignment.Center : HorizontalAlignment.Left };
            var image = Icons.Make(icon, active ? Accent : Muted, 20); image.HorizontalAlignment = HorizontalAlignment.Center; stack.Children.Add(image);
            stack.Children.Add(Text(label, vertical ? 10 : 13, active ? Accent : Muted)); button.Content = stack;
        }
        IReadOnlyList<Track> tracks = _page switch { "library" => _music.Favorites, "queue" => _music.Queue, _ => _music.Results };
        _heading.Text = _page switch { "library" => "Biblioteca", "queue" => "Fila de reprodução", _ => tracks.Count > 0 ? "Resultados" : "Encontre sua música" };
        _count.Text = tracks.Count > 0 ? tracks.Count + " faixas" : "";
        var fingerprint = _page + ":" + string.Join("|", tracks.Select(t => t.Id + _music.IsFavorite(t)));
        if (fingerprint != _listFingerprint)
        {
        _listFingerprint = fingerprint; _rows.Clear(); _tracks.Children.Clear();
        if (tracks.Count == 0)
        {
            var empty = new StackPanel { Spacing = 12, Margin = new Thickness(12, 46), HorizontalAlignment = HorizontalAlignment.Center };
            var icon = Icons.Make(_page == "library" ? "heart" : "wave", Accent, 32); icon.HorizontalAlignment = HorizontalAlignment.Center; empty.Children.Add(icon);
            var message = Text(_page switch { "library" => "Suas favoritas ficam aqui.", "queue" => "Adicione músicas à fila.", _ => "Busque uma música ou artista\npara começar a ouvir." }, 13, Muted);
            message.TextAlignment = TextAlignment.Center; empty.Children.Add(message);
            var import = ActionButton("Abrir áudio local", OpenLocalAsync); import.Classes.Add("subtle"); empty.Children.Add(import); _tracks.Children.Add(empty);
        }
        else foreach (var track in tracks) _tracks.Children.Add(TrackRow(track));
        }
        var displayed = _music.Pending ?? _music.Current;
        _mini.IsVisible = displayed is not null && !_showingPlayer;
        foreach (var (id, row) in _rows) row.Background = Brush.Parse(id == displayed?.Id ? "#251E32" : "Transparent");
        if (!Equals(displayed, _shownTrack)) { _shownTrack = displayed; _nowArt.Content = new Artwork(displayed, 40); }
        _nowTitle.Text = displayed?.Title;
        _nowArtist.Text = _music.LoadingAudio ? _music.Status : displayed?.Artist;
        if (_detailPlay is not null && displayed is not null && !Equals(_detailTrack, displayed)) ShowPlayer();
        RefreshPlayback();
    }
    private Control TrackRow(Track track)
    {
        var grid = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 4, Height = 68 };
        var play = ActionButton("", async () => await _music.PlayAsync(track)); play.Classes.Add("subtle"); play.Padding = new Thickness(0, 8); play.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        var metadata = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 12 };
        metadata.Children.Add(new Artwork(track, 46));
        var labels = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        var title = Text(track.Title, 13); title.TextTrimming = TextTrimming.CharacterEllipsis; labels.Children.Add(title);
        var artist = Text(track.Artist + " · " + track.DurationLabel, 11, Muted); artist.TextTrimming = TextTrimming.CharacterEllipsis; labels.Children.Add(artist);
        Put(metadata, labels, 0, 1); play.Content = metadata; grid.Children.Add(play);
        var more = IconButton("more", "Opções de " + track.Title, () => Task.CompletedTask);
        var favorite = new MenuItem { Header = _music.IsFavorite(track) ? "Remover dos favoritos" : "Salvar nos favoritos" };
        favorite.Click += async (_, _) => await _music.ToggleFavoriteAsync(track);
        var queue = new MenuItem { Header = "Adicionar à fila" }; queue.Click += (_, _) => _music.Enqueue(track);
        var menu = new ContextMenu { ItemsSource = new[] { favorite, queue } };
        more.Click += (_, _) => menu.Open(more); Put(grid, more, 0, 1);
        var border = new Border { Child = grid, Padding = new Thickness(6, 0), CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 2) };
        _rows[track.Id] = border; return border;
    }
    private void ShowPlayer()
    {
        var track = _music.Pending ?? _music.Current; if (track is null) return;
        _detailTrack = track; _showingPlayer = true;
        if (_playerPage is not null) _root.Children.Remove(_playerPage);
        var panel = new StackPanel { Spacing = 16, MaxWidth = 420, Margin = new Thickness(24, 14, 24, 24), HorizontalAlignment = HorizontalAlignment.Stretch };
        var header = new Grid { ColumnDefinitions = new("Auto,*,Auto") };
        header.Children.Add(IconButton("back", "Voltar", () => { ClosePlayer(); return Task.CompletedTask; }));
        var eyebrow = Text("REPRODUZINDO", 10, Muted); eyebrow.LetterSpacing = 2; eyebrow.HorizontalAlignment = HorizontalAlignment.Center; eyebrow.VerticalAlignment = VerticalAlignment.Center; Put(header, eyebrow, 0, 1);
        Put(header, IconButton("queue", "Abrir fila", () => { ClosePlayer(); _page = "queue"; Refresh(); return Task.CompletedTask; }), 0, 2); panel.Children.Add(header);
        var art = new Artwork(track, Math.Clamp(Bounds.Height * 0.32, 120, 280)) { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 16) }; panel.Children.Add(art);
        var metadata = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 12 };
        var labels = new StackPanel { Spacing = 7 };
        var title = Text(track.Title, 20); title.FontWeight = FontWeight.SemiBold; title.TextWrapping = TextWrapping.Wrap; title.MaxLines = 2; labels.Children.Add(title);
        var artist = Text(track.Artist, 13, Muted); artist.TextTrimming = TextTrimming.CharacterEllipsis; labels.Children.Add(artist); metadata.Children.Add(labels);
        Put(metadata, IconButton("heart", "Alternar favorito", async () => { await _music.ToggleFavoriteAsync(track); ShowPlayer(); }, _music.IsFavorite(track) ? Accent : Muted), 0, 1); panel.Children.Add(metadata);
        _detailSeek = new Slider { Minimum = 0, Maximum = 1, Margin = new Thickness(0, 8, 0, 0) };
        _detailSeek.AddHandler(PointerPressedEvent, (_, _) => _seeking = true, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _detailSeek.AddHandler(PointerReleasedEvent, (_, _) => { _seeking = false; _music.Player.Seek(_detailSeek.Value); }, Avalonia.Interactivity.RoutingStrategies.Bubble, true);
        _detailSeek.KeyUp += (_, _) => _music.Player.Seek(_detailSeek.Value); panel.Children.Add(_detailSeek);
        _detailTime = Text("", 11, Muted); panel.Children.Add(_detailTime);
        var controls = new Grid { ColumnDefinitions = new("*,Auto,Auto,Auto,*"), ColumnSpacing = 14, Margin = new Thickness(0, 6) };
        var shuffle = IconButton("shuffle", "Aleatório", () => { _music.Shuffle = !_music.Shuffle; ShowPlayer(); return Task.CompletedTask; }, _music.Shuffle ? Accent : Muted); shuffle.HorizontalAlignment = HorizontalAlignment.Center; controls.Children.Add(shuffle);
        Put(controls, IconButton("previous", "Anterior", _music.PreviousAsync), 0, 1);
        _detailPlay = IconButton("play", "Reproduzir ou pausar", _music.ToggleAsync); _detailPlay.Classes.Remove("subtle"); _detailPlay.Classes.Add("primary"); _detailPlay.Width = _detailPlay.Height = 64; _detailPlay.CornerRadius = new CornerRadius(32); Put(controls, _detailPlay, 0, 2);
        Put(controls, IconButton("next", "Próxima", _music.NextAsync), 0, 3);
        var repeat = IconButton("repeat", "Repetir", () => { _music.Repeat = !_music.Repeat; ShowPlayer(); return Task.CompletedTask; }, _music.Repeat ? Accent : Muted); repeat.HorizontalAlignment = HorizontalAlignment.Center; Put(controls, repeat, 0, 4); panel.Children.Add(controls);
        var queue = ActionButton("Fila de reprodução  ·  " + _music.Queue.Count, () => { ClosePlayer(); _page = "queue"; Refresh(); return Task.CompletedTask; }); queue.Classes.Add("subtle"); queue.HorizontalAlignment = HorizontalAlignment.Center; panel.Children.Add(queue);
        var scroll = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        _playerPage = new Border { Background = Brush.Parse("#15111D"), Child = scroll };
        Grid.SetColumn(_playerPage, 1); Grid.SetRowSpan(_playerPage, 3); _root.Children.Add(_playerPage);
        _main.IsVisible = false; _mini.IsVisible = false; _bottom.IsVisible = false;
        Animate(_playerPage); RefreshPlayback();
    }
    private void ClosePlayer()
    {
        if (_playerPage is not null) _root.Children.Remove(_playerPage);
        _playerPage = null; _showingPlayer = false; _main.IsVisible = true; _bottom.IsVisible = Bounds.Width < 800;
        _detailTrack = null; _detailSeek = null; _detailPlay = null; _detailTime = null; _seeking = false; Refresh(); Animate(_main);
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
        var icon = _music.LoadingAudio ? "close" : _music.Player.IsPlaying ? "pause" : "play";
        _play.Content = Icons.Make(icon, Accent, 20);
        if (_detailPlay is not null) _detailPlay.Content = Icons.Make(icon, "#211337", 20);
        var duration = _music.Player.Duration; if (duration <= 0) duration = _music.Current?.DurationSeconds ?? 0;
        _progress.Maximum = Math.Max(1, duration); _progress.Value = Math.Min(_music.Player.Position, _progress.Maximum);
        if (_detailSeek is not null)
        {
            _detailSeek.Maximum = Math.Max(1, duration); _detailSeek.IsEnabled = duration > 0 && !_music.LoadingAudio;
            if (!_seeking) _detailSeek.Value = Math.Min(_music.Player.Position, _detailSeek.Maximum);
        }
        if (_detailTime is not null) _detailTime.Text = Time(_music.Player.Position) + "  /  " + Time(duration);
    }
    private void ShowSettings()
    {
        var panel = new StackPanel { Spacing = 12 };
        var session = System.IO.File.Exists(CookiesPath);
        panel.Children.Add(Text("YouTube", 15));
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
        panel.Children.Add(new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = Brush.Parse(Muted) });
        panel.Children.Add(ActionButton("Copiar diagnóstico", async () => { var clipboard = TopLevel.GetTopLevel(this)?.Clipboard; if (clipboard is not null) await clipboard.SetTextAsync(PlaybackDiagnostics.Snapshot()); }));
        ShowSheet("Diagnóstico", panel);
    }
    private void ShowSheet(string title, Control content)
    {
        if (_overlay is not null) _root.Children.Remove(_overlay);
        var layout = new Grid { RowDefinitions = new("Auto,*"), RowSpacing = 16 };
        var header = new Grid { ColumnDefinitions = new("*,Auto") }; var label = Text(title, 17); label.VerticalAlignment = VerticalAlignment.Center; header.Children.Add(label);
        Put(header, IconButton("close", "Fechar painel", () => { CloseSheet(); return Task.CompletedTask; }), 0, 1); layout.Children.Add(header);
        Put(layout, new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, 1, 0);
        var sheet = new Border { Width = Math.Max(280, Math.Min(440, Bounds.Width - 24)), MaxHeight = Math.Max(320, Bounds.Height - 32), Padding = new Thickness(20), CornerRadius = new CornerRadius(20), Background = Brush.Parse("#1B1722"), Child = layout, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _overlay = new Border { Background = Brush.Parse("#BA000000"), Child = sheet };
        Grid.SetColumnSpan(_overlay, 2); Grid.SetRowSpan(_overlay, 3); _root.Children.Add(_overlay); Animate(sheet);
    }
    private void CloseSheet()
    {
        if (_overlay is not null) _root.Children.Remove(_overlay); _overlay = null;
        _detailTrack = null; _detailSeek = null; _detailPlay = null; _detailTime = null; _seeking = false;
    }
    private static string Time(double seconds) => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss");
    private static TextBlock Text(string text, double size, string color = "#F3EFF8") => new() { Text = text, FontSize = size, Foreground = Brush.Parse(color) };
    private Button ActionButton(string text, Func<Task> action)
    {
        var button = new Button { Content = text };
        button.Click += async (_, _) => { try { await action(); } catch (Exception ex) { PlaybackDiagnostics.Write("Interface", ex.Message); _music.SetStatus(ex.Message); ShowDiagnostics(); } }; return button;
    }
    private Button IconButton(string icon, string label, Func<Task> action, string color = Muted)
    {
        var button = ActionButton("", action); button.Content = Icons.Make(icon, color, 20); button.Classes.Add("subtle"); button.Padding = new Thickness(10); button.Width = button.Height = 44; button.VerticalAlignment = VerticalAlignment.Center;
        Avalonia.Automation.AutomationProperties.SetName(button, label); ToolTip.SetTip(button, label); return button;
    }
    private static void Put(Grid grid, Control control, int row, int column) { Grid.SetRow(control, row); Grid.SetColumn(control, column); grid.Children.Add(control); }
}
