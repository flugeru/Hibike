using Avalonia;
using Avalonia.Animation;
using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
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
    private readonly TextBox _search = UiTheme.Field("Música, artista ou link");
    private readonly TextBlock _heading = Text("Resultados", UiTheme.SectionTitle);
    private readonly TextBlock _count = Text("", UiTheme.Label, Muted);
    private readonly ItemsControl _tracks = new() { ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()) };
    private readonly TextBlock _status = Text("", UiTheme.Caption, Muted);
    private readonly Border _statusBox;
    private readonly Button _clearSearch;
    private readonly Button _cancelSearch;
    private readonly Border _mini;
    private readonly ContentControl _nowArt = new();
    private readonly TextBlock _nowTitle = Text("", UiTheme.Body);
    private readonly TextBlock _nowArtist = Text("", UiTheme.Caption, Muted);
    private readonly Button _play;
    private readonly ProgressBar _progress = new() { Height = UiTheme.MiniProgressHeight, Minimum = 0, Maximum = 1 };
    private readonly Grid _bottom;
    private readonly List<Button> _nav = [];
    private Border? _overlay;
    private Border? _playerPage;
    private readonly Dictionary<string, Border> _rows = new();
    private readonly Dictionary<string, TextBlock> _rowTitles = new();
    private readonly Dictionary<string, Equalizer> _equalizers = new();
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
    private const string Muted = UiTheme.TextSecondary, Accent = UiTheme.Primary;

    public MainView(MusicController music)
    {
        _music = music; Background = UiTheme.Brush(UiTheme.Background);
        TopLevel.SetAutoSafeAreaPadding(this, true);
        _sidebar = BuildSidebar(); _root.Children.Add(_sidebar); Grid.SetRowSpan(_sidebar, 3);
        _main = new Border { Padding = UiTheme.PageMargin, MaxWidth = UiTheme.PageMaxWidth, HorizontalAlignment = HorizontalAlignment.Stretch }; Put(_root, _main, 0, 1);
        var content = new Grid { RowDefinitions = new("Auto,Auto,Auto,*,Auto"), RowSpacing = UiTheme.PageGap }; _main.Child = content;
        var header = new Grid { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = UiTheme.CompactGap };
        var title = UiTheme.Text("Hibiki", UiTheme.PageTitle, weight: FontWeight.SemiBold); header.Children.Add(title);
        Put(header, IconButton("folder", "Abrir áudio local", OpenLocalAsync), 0, 1);
        Put(header, IconButton("settings", "Configurações e diagnóstico", () => { ShowSettings(); return Task.CompletedTask; }), 0, 2);
        content.Children.Add(header);
        var searchRow = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = UiTheme.CompactGap };
        searchRow.Children.Add(_search);
        _search.InnerLeftContent = Icons.Make("search", Muted);
        _clearSearch = IconButton("close", "Limpar busca", () => { _search.Text = ""; _search.Focus(); return Task.CompletedTask; });
        _clearSearch.Classes.Add("subtle"); _clearSearch.IsVisible = false; _search.InnerRightContent = _clearSearch;
        _search.TextChanged += (_, _) => _clearSearch.IsVisible = !string.IsNullOrEmpty(_search.Text);
        _cancelSearch = IconButton("close", "Cancelar busca", () => { _music.CancelSearch(); return Task.CompletedTask; }); Put(searchRow, _cancelSearch, 0, 1);
        _search.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await SearchAsync(); } };
        Put(content, searchRow, 1, 0);
        var heading = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = UiTheme.CompactGap };
        _heading.TextTrimming = TextTrimming.CharacterEllipsis; _heading.FontWeight = FontWeight.SemiBold;
        _count.VerticalAlignment = VerticalAlignment.Center; heading.Children.Add(_heading); Put(heading, UiTheme.Chip(_count), 0, 1); Put(content, heading, 2, 0);
        _tracks.ItemTemplate = new FuncDataTemplate<Track>((track, _) => TrackRow(track!));
        Put(content, new ScrollViewer { Content = _tracks, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden }, 3, 0);
        var statusRow = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = UiTheme.CompactGap };
        _status.TextWrapping = TextWrapping.Wrap; _status.MaxLines = 2; _status.VerticalAlignment = VerticalAlignment.Center; statusRow.Children.Add(_status);
        Put(statusRow, IconButton("info", "Ver diagnóstico", () => { ShowDiagnostics(); return Task.CompletedTask; }), 0, 1);
        _statusBox = new Border { Child = statusRow }; UiTheme.StateBanner(_statusBox, UiTheme.Warning); Put(content, _statusBox, 4, 0);
        _play = IconButton("play", "Reproduzir, pausar ou cancelar", _music.ToggleAsync, UiTheme.OnPrimary); _play.Classes.Add("primary");
        _mini = BuildMiniPlayer(); Put(_root, _mini, 1, 1);
        _bottom = new Grid { ColumnDefinitions = new("*,*,*"), Height = UiTheme.NavHeight, Background = UiTheme.Brush(UiTheme.Surface) };
        var tabs = new[] { ("explore", "Buscar", "search"), ("library", "Biblioteca", "library"), ("queue", "Fila", "queue") };
        for (var index = 0; index < tabs.Length; index++)
        {
            var (key, label, icon) = tabs[index];
            var button = NavButton(key, label, icon, true); Put(_bottom, button, 0, index);
        }
        Put(_root, new Border { Child = _bottom, BorderBrush = UiTheme.Brush(UiTheme.Outline), BorderThickness = UiTheme.TopBorder }, 2, 1); Content = _root;
        SizeChanged += (_, _) =>
        {
            var compact = Bounds.Width < UiTheme.DesktopWidth;
            _root.ColumnDefinitions[0].Width = new GridLength(compact ? 0 : UiTheme.SidebarWidth);
            _sidebar.IsVisible = !compact; _bottom.IsVisible = compact;
            _main.IsVisible = !_showingPlayer;
            _bottom.IsVisible = compact && !_showingPlayer;
            _main.Padding = UiTheme.PageMargin;
            header.RowDefinitions = new(Bounds.Width < UiTheme.CompactWidth ? "Auto,Auto" : "Auto");
            Grid.SetRow(title, 0); Grid.SetColumnSpan(title, Bounds.Width < UiTheme.CompactWidth ? 3 : 1);
            Grid.SetRow(header.Children[1], Bounds.Width < UiTheme.CompactWidth ? 1 : 0);
            Grid.SetRow(header.Children[2], Bounds.Width < UiTheme.CompactWidth ? 1 : 0);
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
        var panel = new StackPanel { Spacing = UiTheme.CompactGap, Margin = UiTheme.PageMargin };
        var title = Text("Sua música", UiTheme.SectionTitle); panel.Children.Add(title);
        panel.Children.Add(NavButton("explore", "Buscar", "search", false));
        panel.Children.Add(NavButton("library", "Biblioteca", "library", false));
        panel.Children.Add(NavButton("queue", "Fila", "queue", false));
        return new Border { Child = panel, Background = UiTheme.Brush(UiTheme.Surface) };
    }
    private Button NavButton(string key, string label, string icon, bool vertical)
    {
        var button = ActionButton("", () => { _page = key; Refresh(); Animate(_tracks); return Task.CompletedTask; });
        button.Classes.Add("nav"); button.Tag = (key, label, icon, vertical); _nav.Add(button);
        button.Padding = UiTheme.Zero; button.HorizontalContentAlignment = vertical ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        return button;
    }
    private Border BuildMiniPlayer()
    {
        var grid = new Grid { RowDefinitions = new("Auto,Auto"), RowSpacing = UiTheme.CompactGap };
        var row = new Grid { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = UiTheme.ProgressRadius };
        var open = ActionButton("", () => { ShowPlayer(); return Task.CompletedTask; }); Avalonia.Automation.AutomationProperties.SetName(open, "Abrir player"); open.Classes.Add("subtle"); open.Padding = UiTheme.Zero; open.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        var metadata = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = UiTheme.CompactGap };
        _nowArt.Content = new Artwork(null, UiTheme.MiniArtworkSize); metadata.Children.Add(_nowArt);
        var info = new StackPanel { Spacing = UiTheme.ProgressRadius, VerticalAlignment = VerticalAlignment.Center };
        _nowTitle.TextTrimming = _nowArtist.TextTrimming = TextTrimming.CharacterEllipsis; info.Children.Add(_nowTitle); info.Children.Add(_nowArtist); Put(metadata, info, 0, 1);
        open.Content = metadata; row.Children.Add(open); Put(row, _play, 0, 1); Put(row, IconButton("next", "Próxima faixa", _music.NextAsync), 0, 2);
        grid.Children.Add(row); Put(grid, _progress, 1, 0);
        var card = UiTheme.Card(grid, UiTheme.ControlRadius); card.Margin = UiTheme.MiniMargin; card.ClipToBounds = true;
        card.MaxWidth = UiTheme.PageMaxWidth - UiTheme.PageMargin.Left - UiTheme.PageMargin.Right; return card;
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
        UiTheme.StateBanner(_statusBox, _music.HasError ? UiTheme.Error : UiTheme.Warning);
        _status.Text = _music.Status; _cancelSearch.IsVisible = _music.Searching;
        foreach (var button in _nav)
        {
            var (key, label, icon, vertical) = ((string, string, string, bool))button.Tag!;
            var active = key == _page;
            var stack = new StackPanel { Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal, Spacing = UiTheme.ProgressRadius, HorizontalAlignment = vertical ? HorizontalAlignment.Center : HorizontalAlignment.Left };
            var image = Icons.Make(icon, active ? Accent : Muted); image.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(new Border { Child = image, Background = active ? UiTheme.Brush(UiTheme.PrimaryContainer) : Brushes.Transparent,
                CornerRadius = new(UiTheme.ControlRadius), Padding = UiTheme.ChipPadding, HorizontalAlignment = HorizontalAlignment.Center });
            stack.Children.Add(UiTheme.Text(label, UiTheme.Label, active ? UiTheme.TextPrimary : Muted, FontWeight.Medium)); button.Content = stack;
        }
        IReadOnlyList<Track> tracks = _page switch { "library" => _music.Favorites, "queue" => _music.Queue, _ => _music.Results };
        _heading.Text = _page switch { "library" => "Biblioteca", "queue" => "Fila de reprodução", _ => "Resultados" };
        _count.Text = tracks.Count + (tracks.Count == 1 ? " faixa" : " faixas");
        var fingerprint = _page + ":" + string.Join("|", tracks.Select(t => t.Id + _music.IsFavorite(t)));
        if (fingerprint != _listFingerprint)
        {
        _listFingerprint = fingerprint; _rows.Clear(); _rowTitles.Clear(); _equalizers.Clear(); _tracks.ItemsSource = tracks;
        if (tracks.Count == 0)
        {
            if (!_music.Searching && !_music.LoadingAudio && !_music.HasError)
            {
                _statusBox.IsVisible = true; UiTheme.StateBanner(_statusBox, UiTheme.Success);
                _status.Text = _page switch { "library" => "Suas favoritas ficam aqui. Abra um áudio local ou salve uma faixa.", "queue" => "Adicione músicas à fila pelo menu de cada faixa.", _ => "Busque uma música ou artista para começar a ouvir." };
            }
        }
        }
        var displayed = _music.Pending ?? _music.Current;
        _mini.IsVisible = displayed is not null && !_showingPlayer;
        UpdateRows(displayed);
        if (!Equals(displayed, _shownTrack)) { _shownTrack = displayed; _nowArt.Content = new Artwork(displayed, UiTheme.MiniArtworkSize); }
        _nowTitle.Text = TrackTextFormatter.Title(displayed?.Title, displayed?.Artist);
        _nowArtist.Text = _music.LoadingAudio ? _music.Status : TrackTextFormatter.Artist(displayed?.Artist);
        if (_detailPlay is not null && displayed is not null && !Equals(_detailTrack, displayed)) ShowPlayer();
        RefreshPlayback();
    }
    private Control TrackRow(Track track)
    {
        var grid = new Grid { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = UiTheme.CompactGap, MinHeight = UiTheme.RowHeight };
        var play = ActionButton("", async () => await _music.PlayAsync(track)); play.Classes.Add("subtle"); play.Padding = UiTheme.Zero; play.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Avalonia.Automation.AutomationProperties.SetName(play, track.Title + " — " + track.Artist);
        var metadata = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = UiTheme.CompactGap };
        var thumbnail = new Grid(); thumbnail.Children.Add(new Artwork(track, UiTheme.ThumbnailSize));
        var equalizer = new Equalizer(); thumbnail.Children.Add(equalizer); metadata.Children.Add(thumbnail);
        var labels = new StackPanel { Spacing = UiTheme.ProgressRadius, VerticalAlignment = VerticalAlignment.Center };
        var title = Text(TrackTextFormatter.Title(track.Title, track.Artist), UiTheme.Body); title.TextTrimming = TextTrimming.CharacterEllipsis; labels.Children.Add(title);
        var artist = Text(TrackTextFormatter.Artist(track.Artist), UiTheme.Caption, Muted); artist.TextTrimming = TextTrimming.CharacterEllipsis; labels.Children.Add(artist);
        Put(metadata, labels, 0, 1); play.Content = metadata;
        grid.Children.Add(play);
        Put(grid, Text(track.DurationLabel, UiTheme.Caption, Muted), 0, 1);
        var more = IconButton("more", "Opções de " + track.Title, () => Task.CompletedTask);
        more.Click += (_, _) =>
        {
            var actions = new StackPanel { Spacing = UiTheme.CompactGap };
            actions.Children.Add(ActionButton(_music.IsFavorite(track) ? "Remover dos favoritos" : "Salvar nos favoritos", async () => { await _music.ToggleFavoriteAsync(track); CloseSheet(); }));
            actions.Children.Add(ActionButton("Adicionar à fila", () => { _music.Enqueue(track); CloseSheet(); return Task.CompletedTask; }));
            ShowSheet(TrackTextFormatter.Title(track.Title, track.Artist), actions);
        }; Put(grid, more, 0, 2);
        var border = new Border { Child = grid, Padding = UiTheme.RowPadding, CornerRadius = new(UiTheme.ControlRadius) };
        _rows[track.Id] = border; _rowTitles[track.Id] = title; _equalizers[track.Id] = equalizer;
        border.DetachedFromVisualTree += (_, _) =>
        {
            if (_rows.GetValueOrDefault(track.Id) == border) { _rows.Remove(track.Id); _rowTitles.Remove(track.Id); _equalizers.Remove(track.Id); }
        };
        UpdateRows(_music.Pending ?? _music.Current);
        return border;
    }
    private void UpdateRows(Track? displayed)
    {
        foreach (var (id, row) in _rows)
        {
            var selected = id == displayed?.Id;
            row.Background = UiTheme.Brush(selected ? UiTheme.PrimaryContainer : UiTheme.Surface);
            row.BorderBrush = UiTheme.Brush(selected ? UiTheme.Primary : UiTheme.Outline);
            row.BorderThickness = selected ? UiTheme.One : UiTheme.BottomBorder;
            _rowTitles[id].FontWeight = selected ? FontWeight.SemiBold : FontWeight.Normal;
            _equalizers[id].SetState(selected, selected && _music.Player.IsPlaying);
        }
    }
    private void ShowPlayer()
    {
        var track = _music.Pending ?? _music.Current; if (track is null) return;
        if (_overlay is not null) CloseSheet();
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
        var title = Text(TrackTextFormatter.Title(track.Title, track.Artist), UiTheme.DialogTitle); title.FontWeight = FontWeight.SemiBold; title.TextWrapping = TextWrapping.Wrap; title.MaxLines = 2; labels.Children.Add(title);
        var artist = Text(TrackTextFormatter.Artist(track.Artist), UiTheme.Body, Muted); artist.TextTrimming = TextTrimming.CharacterEllipsis; labels.Children.Add(artist); metadata.Children.Add(labels);
        Button? favorite = null;
        favorite = IconButton("heart", "Alternar favorito", async () => { await _music.ToggleFavoriteAsync(track); favorite!.Content = Icons.Make("heart", _music.IsFavorite(track) ? Accent : Muted, 20); }, _music.IsFavorite(track) ? Accent : Muted);
        Put(metadata, favorite, 0, 1); panel.Children.Add(metadata);
        _detailSeek = new Slider { Minimum = 0, Maximum = 1, Margin = new Thickness(10, 8, 10, 0) };
        _detailSeek.AddHandler(PointerPressedEvent, (_, _) => _seeking = true, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _detailSeek.AddHandler(PointerReleasedEvent, (_, _) => { _seeking = false; _music.Player.Seek(_detailSeek.Value); }, Avalonia.Interactivity.RoutingStrategies.Bubble, true);
        _detailSeek.KeyUp += (_, _) => _music.Player.Seek(_detailSeek.Value); panel.Children.Add(_detailSeek);
        var times = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(10, 0) };
        _detailTime = Text("", 11, Muted); _detailDuration = Text("", 11, Muted);
        times.Children.Add(_detailTime); Put(times, _detailDuration, 0, 1); panel.Children.Add(times);
        var controls = new Grid { ColumnDefinitions = new("*,Auto,Auto,Auto,*"), ColumnSpacing = 14, Margin = new Thickness(0, 6) };
        Button? shuffle = null;
        shuffle = IconButton("shuffle", "Aleatório", () => { _music.Shuffle = !_music.Shuffle; shuffle!.Content = Icons.Make("shuffle", _music.Shuffle ? Accent : Muted, 20); return Task.CompletedTask; }, _music.Shuffle ? Accent : Muted); shuffle.HorizontalAlignment = HorizontalAlignment.Center; controls.Children.Add(shuffle);
        Put(controls, IconButton("previous", "Anterior", _music.PreviousAsync), 0, 1);
        _detailPlay = IconButton("play", "Reproduzir ou pausar", _music.ToggleAsync); _detailPlay.Classes.Remove("subtle"); _detailPlay.Classes.Add("primary"); _detailPlay.Width = _detailPlay.Height = 64; _detailPlay.CornerRadius = new CornerRadius(32); Put(controls, _detailPlay, 0, 2);
        Put(controls, IconButton("next", "Próxima", _music.NextAsync), 0, 3);
        Button? repeat = null;
        repeat = IconButton("repeat", "Repetir", () => { _music.Repeat = !_music.Repeat; repeat!.Content = Icons.Make("repeat", _music.Repeat ? Accent : Muted, 20); return Task.CompletedTask; }, _music.Repeat ? Accent : Muted); repeat.HorizontalAlignment = HorizontalAlignment.Center; Put(controls, repeat, 0, 4); panel.Children.Add(controls);
        var queue = ActionButton("Fila de reprodução  ·  " + _music.Queue.Count, () => { ClosePlayer(); _page = "queue"; Refresh(); return Task.CompletedTask; }); var queueLabel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        queueLabel.Children.Add(Icons.Make("queue", Muted, 16)); queueLabel.Children.Add(Text("Fila · " + _music.Queue.Count, 12, Muted)); queue.Content = queueLabel;
        queue.MinWidth = 140; queue.HorizontalContentAlignment = HorizontalAlignment.Center; queue.HorizontalAlignment = HorizontalAlignment.Center; panel.Children.Add(queue);
        var scroll = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        _playerPage = new Border { Background = Brush.Parse("#15111D"), Child = scroll };
        Grid.SetColumn(_playerPage, 1); Grid.SetRowSpan(_playerPage, 3); _root.Children.Add(_playerPage);
        _main.IsVisible = false; _mini.IsVisible = false; _bottom.IsVisible = false;
        Animate(_playerPage); RefreshPlayback();
    }
    private void ClosePlayer()
    {
        if (_playerPage is not null) _root.Children.Remove(_playerPage);
        _playerPage = null; _showingPlayer = false; _listFingerprint = null; _main.IsVisible = true; _bottom.IsVisible = Bounds.Width < 800;
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
        UpdateRows(_music.Pending ?? _music.Current);
        var icon = _music.LoadingAudio ? "close" : _music.Player.IsPlaying ? "pause" : "play";
        _play.Content = Icons.Make(icon, UiTheme.OnPrimary);
        if (_detailPlay is not null) _detailPlay.Content = Icons.Make(icon, "#211337", 20);
        var duration = _music.Current?.DurationSeconds ?? 0; if (duration <= 0) duration = _music.Player.Duration;
        _progress.Maximum = Math.Max(1, duration); _progress.Value = Math.Min(_music.Player.Position, _progress.Maximum);
        if (_detailSeek is not null)
        {
            _detailSeek.Maximum = Math.Max(1, duration); _detailSeek.IsEnabled = duration > 0 && !_music.LoadingAudio;
            if (!_seeking) _detailSeek.Value = Math.Min(_music.Player.Position, _detailSeek.Maximum);
        }
        if (_detailTime is not null) _detailTime.Text = Time(_music.Player.Position);
        if (_detailDuration is not null) _detailDuration.Text = Time(duration);
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
        var layout = new Grid { RowDefinitions = new("Auto,*,Auto"), RowSpacing = UiTheme.CardGap };
        var label = UiTheme.Text(title, UiTheme.DialogTitle, weight: FontWeight.SemiBold); label.TextWrapping = TextWrapping.Wrap; layout.Children.Add(label);
        Put(layout, new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, 1, 0);
        Put(layout, ActionButton("Fechar", () => { CloseSheet(); return Task.CompletedTask; }), 2, 0);
        var sheet = UiTheme.Card(layout); sheet.MaxWidth = UiTheme.DialogMaxWidth; sheet.Margin = UiTheme.DialogMargin;
        sheet.HorizontalAlignment = HorizontalAlignment.Stretch; sheet.VerticalAlignment = VerticalAlignment.Center;
        sheet.MaxHeight = Math.Max(UiTheme.TouchSize, Bounds.Height - UiTheme.DialogMargin.Top - UiTheme.DialogMargin.Bottom);
        _overlay = new Border { Background = UiTheme.Brush(UiTheme.Scrim), Child = sheet };
        Grid.SetColumnSpan(_overlay, 2); Grid.SetRowSpan(_overlay, 3); _root.Children.Add(_overlay); Animate(sheet);
    }
    private void CloseSheet()
    {
        if (_overlay is not null) _root.Children.Remove(_overlay); _overlay = null;
        _detailTrack = null; _detailSeek = null; _detailPlay = null; _detailTime = null; _detailDuration = null; _seeking = false;
    }
    private static string Time(double seconds) => TrackTextFormatter.Duration(seconds);
    private static TextBlock Text(string text, double size, string color = UiTheme.TextPrimary) => UiTheme.Text(text, size, color);
    private Button ActionButton(string text, Func<Task> action)
    {
        var button = UiTheme.OutlinedButton(text);
        button.Click += async (_, _) => { try { await action(); } catch (Exception ex) { PlaybackDiagnostics.Write("Interface", ex.Message); _music.SetStatus(ex.Message); ShowDiagnostics(); } }; return button;
    }
    private Button IconButton(string icon, string label, Func<Task> action, string color = Muted)
    {
        var button = ActionButton("", action); button.Content = Icons.Make(icon, color); button.Classes.Add("round"); button.VerticalAlignment = VerticalAlignment.Center;
        Avalonia.Automation.AutomationProperties.SetName(button, label); ToolTip.SetTip(button, label); return button;
    }
    private static void Put(Grid grid, Control control, int row, int column) { Grid.SetRow(control, row); Grid.SetColumn(control, column); grid.Children.Add(control); }
}
