using Avalonia;
using Avalonia.Animation;
using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Hibike.Core.Models;
using Hibike.Core.Services;
using Track = Hibike.Core.Models.Track;

namespace Hibike.App.Views;

public sealed class MainView : UserControl
{
    private readonly MusicController _music;
    private readonly Grid _root = new() { ColumnDefinitions = new($"{UiTheme.SidebarWidth},*"), RowDefinitions = new("*,Auto,Auto") };
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
    private readonly Border _bottom;
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
        _main = new Border { Padding = UiTheme.ContentPadding, MaxWidth = UiTheme.PageMaxWidth, HorizontalAlignment = HorizontalAlignment.Stretch }; Put(_root, _main, 0, 1);
        var content = new Grid { RowDefinitions = new("Auto,Auto,Auto,Auto,*"), RowSpacing = UiTheme.PageGap }; _main.Child = content;
        var header = new Grid { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = UiTheme.CompactGap };
        var title = UiTheme.Text("Hibike", UiTheme.PageTitle, weight: FontWeight.SemiBold); header.Children.Add(title);
        Put(header, IconButton("folder", "Abrir áudio local", OpenLocalAsync), 0, 1);
        Put(header, IconButton("settings", "Configurações e diagnóstico", () => { ShowSettings(); return Task.CompletedTask; }), 0, 2);
        content.Children.Add(header);
        var searchRow = new Grid { ColumnDefinitions = new("*") };
        searchRow.Children.Add(_search);
        _search.InnerLeftContent = new Border { Child = Icons.Make("search", Muted), Padding = UiTheme.SearchIconPadding };
        TextInputOptions.SetContentType(_search, TextInputContentType.Search);
        TextInputOptions.SetReturnKeyType(_search, TextInputReturnKeyType.Search);
        _clearSearch = IconButton("close", "Limpar busca", () => { _search.Text = ""; _search.Focus(); return Task.CompletedTask; });
        _clearSearch.Classes.Add("subtle"); _clearSearch.IsVisible = false;
        _search.TextChanged += (_, _) => _clearSearch.IsVisible = !string.IsNullOrEmpty(_search.Text);
        _cancelSearch = IconButton("close", "Cancelar busca", () => { _music.CancelSearch(); return Task.CompletedTask; }); _cancelSearch.Classes.Add("subtle");
        var searchActions = new StackPanel { Orientation = Orientation.Horizontal };
        searchActions.Children.Add(_clearSearch); searchActions.Children.Add(_cancelSearch); _search.InnerRightContent = searchActions;
        _search.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await SearchAsync(); } };
        Put(content, searchRow, 1, 0);
        var heading = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = UiTheme.CompactGap };
        _heading.TextTrimming = TextTrimming.CharacterEllipsis; _heading.FontWeight = FontWeight.SemiBold;
        _count.VerticalAlignment = VerticalAlignment.Center; heading.Children.Add(_heading);
        var countChip = UiTheme.Chip(_count); countChip.VerticalAlignment = VerticalAlignment.Center;
        var headingActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = UiTheme.CompactGap };
        var clearQueue = ActionButton("Limpar fila", () => { _music.ClearQueue(); return Task.CompletedTask; });
        _heading.PropertyChanged += (_, e) => { if (e.Property == TextBlock.TextProperty) clearQueue.IsVisible = _page == "queue"; };
        clearQueue.IsVisible = false;
        headingActions.Children.Add(clearQueue); headingActions.Children.Add(countChip);
        Put(heading, headingActions, 0, 1); Put(content, heading, 2, 0);
        _tracks.ItemTemplate = new FuncDataTemplate<Track>((track, _) => track is null ? new Border() : TrackRow(track));
        var trackScroll = new ScrollViewer { Content = _tracks, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
        trackScroll.SizeChanged += (_, _) => trackScroll.OpacityMask = UiTheme.ListMask(trackScroll.Bounds.Height);
        Put(content, trackScroll, 4, 0);
        var statusRow = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = UiTheme.CompactGap };
        _status.TextWrapping = TextWrapping.Wrap; _status.MaxLines = 2; _status.VerticalAlignment = VerticalAlignment.Center; statusRow.Children.Add(_status);
        Put(statusRow, IconButton("info", "Ver diagnóstico", () => { ShowDiagnostics(); return Task.CompletedTask; }), 0, 1);
        _statusBox = new Border { Child = statusRow }; UiTheme.StateBanner(_statusBox, UiTheme.Warning); Put(content, _statusBox, 3, 0);
        _play = IconButton("play", "Reproduzir, pausar ou cancelar", _music.ToggleAsync, UiTheme.OnPrimary); _play.Classes.Add("primary"); _play.Classes.Add("miniPlay");
        _mini = BuildMiniPlayer(); Put(_root, _mini, 1, 1);
        var navGrid = new Grid { ColumnDefinitions = new("*,*,*"), Height = UiTheme.NavHeight, Background = UiTheme.Brush(UiTheme.Surface) };
        var tabs = new[] { ("explore", "Buscar", "search"), ("library", "Biblioteca", "library"), ("queue", "Fila", "queue") };
        for (var index = 0; index < tabs.Length; index++)
        {
            var (key, label, icon) = tabs[index];
            var button = NavButton(key, label, icon, true); Put(navGrid, button, 0, index);
        }
        _bottom = new Border { Child = navGrid, BorderBrush = UiTheme.Brush(UiTheme.Outline), BorderThickness = UiTheme.TopBorder };
        Put(_root, _bottom, 2, 1); Content = _root;
        SizeChanged += (_, _) =>
        {
            var compact = Bounds.Width < UiTheme.DesktopWidth;
            _root.ColumnDefinitions[0].Width = new GridLength(compact ? 0 : UiTheme.SidebarWidth);
            _sidebar.IsVisible = !compact; _bottom.IsVisible = compact;
            _main.IsVisible = !_showingPlayer;
            _bottom.IsVisible = compact && !_showingPlayer;
            _main.Padding = UiTheme.ContentPadding;
            // Title and two 48dp actions fit at 360dp; no artificial second toolbar row.
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
                var track = await Task.Run(() => LocalTrackMetadata.Read(new Track(id, System.IO.Path.GetFileNameWithoutExtension(fileName), "Áudio local", path, IsLocal: true), System.IO.Path.Combine(_music.DataDirectory, "artwork")));
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
            button.Background = active ? UiTheme.Brush(UiTheme.PrimaryContainer) : Brushes.Transparent;
            button.CornerRadius = new(UiTheme.ControlRadius); button.Padding = UiTheme.NavPadding;
            var stack = new StackPanel { Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal, Spacing = UiTheme.ProgressRadius, HorizontalAlignment = vertical ? HorizontalAlignment.Center : HorizontalAlignment.Left };
            var image = Icons.Make(icon, active ? Accent : Muted); image.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(image);
            stack.Children.Add(UiTheme.Text(label, UiTheme.Label, active ? UiTheme.TextPrimary : Muted, FontWeight.Medium)); button.Content = stack;
        }
        IReadOnlyList<Track> tracks = _page switch { "library" => _music.Favorites, "queue" => _music.Queue, _ => _music.Results };
        _heading.Text = _page switch { "library" => "Biblioteca", "queue" => "Fila de reprodução", _ => "Resultados" };
        _count.Text = tracks.Count + (tracks.Count == 1 ? " faixa" : " faixas");
        var fingerprint = _page + ":" + string.Join("|", tracks.Select(t => t.Id + _music.IsFavorite(t)));
        if (fingerprint != _listFingerprint)
        {
        _listFingerprint = fingerprint; _rows.Clear(); _rowTitles.Clear(); _equalizers.Clear(); _tracks.ItemsSource = tracks;
        }
        if (tracks.Count == 0 && !_music.Searching && !_music.LoadingAudio && !_music.HasError)
            {
                _statusBox.IsVisible = true; UiTheme.StateBanner(_statusBox, UiTheme.Success);
                _status.Text = _page switch { "library" => "Suas favoritas ficam aqui. Abra um áudio local ou salve uma faixa.", "queue" => "Adicione músicas à fila pelo menu de cada faixa.", _ => "Busque uma música ou artista para começar a ouvir." };
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
        var grid = new Grid { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = UiTheme.CompactGap, Height = UiTheme.ThumbnailSize };
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
        more.Classes.Add("subtle");
        var border = new Border { Child = grid, Padding = UiTheme.RowPadding, Height = UiTheme.TrackRowHeight };
        _rows[track.Id] = border; _rowTitles[track.Id] = title; _equalizers[track.Id] = equalizer;
        border.AttachedToVisualTree += (_, _) =>
        {
            _rows[track.Id] = border; _rowTitles[track.Id] = title; _equalizers[track.Id] = equalizer;
            UpdateRows(_music.Pending ?? _music.Current);
        };
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
            row.Background = selected ? UiTheme.Brush(UiTheme.PrimaryContainer) : Brushes.Transparent;
            row.BorderBrush = UiTheme.Brush(selected ? UiTheme.Primary : UiTheme.Outline);
            row.BorderThickness = selected ? UiTheme.One : UiTheme.BottomBorder;
            row.CornerRadius = new(selected ? UiTheme.ControlRadius : 0);
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
        var panel = new Grid { RowDefinitions = new("Auto,*,Auto,Auto,*,Auto,Auto"),
            RowSpacing = UiTheme.SectionGap, MaxWidth = UiTheme.PlayerMaxWidth,
            Margin = UiTheme.PageMargin, HorizontalAlignment = HorizontalAlignment.Stretch };
        var header = new Grid { ColumnDefinitions = new("Auto,*,Auto,Auto") };
        header.Children.Add(IconButton("down", "Recolher player", () => { ClosePlayer(); return Task.CompletedTask; }));
        var album = Text(track.Album ?? TrackTextFormatter.Artist(track.Artist), UiTheme.Caption, Muted); album.TextTrimming = TextTrimming.CharacterEllipsis; album.HorizontalAlignment = HorizontalAlignment.Center; Put(header, album, 0, 1);
        Put(header, IconButton("queue", "Abrir fila", () => { ClosePlayer(); _page = "queue"; Refresh(); return Task.CompletedTask; }), 0, 2); panel.Children.Add(header);
        var artworkTrack = track == _music.Current && _music.CurrentSource?.Artwork is { } resolvedArtwork ? track with { Thumbnail = resolvedArtwork } : track;
        var art = new Artwork(artworkTrack, UiTheme.ThumbnailSize, fullResolution: true) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        art.CornerRadius = new(UiTheme.CardRadius); Put(panel, art, 1, 0);
        var metadata = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = UiTheme.CompactGap };
        var labels = new StackPanel { Spacing = UiTheme.CompactGap };
        var title = Text(TrackTextFormatter.Title(track.Title, track.Artist), UiTheme.DialogTitle); title.FontWeight = FontWeight.SemiBold; title.TextWrapping = TextWrapping.Wrap; title.MaxLines = 2; labels.Children.Add(title);
        var artist = Text(TrackTextFormatter.Artist(track.Artist), UiTheme.Body, Muted); artist.TextTrimming = TextTrimming.CharacterEllipsis; labels.Children.Add(artist); metadata.Children.Add(labels);
        Button? favorite = null;
        void UpdateFavorite()
        {
            var active = _music.IsFavorite(track); var icon = Icons.Make("heart", active ? Accent : Muted, filled: active);
            favorite!.Content = icon; favorite.Background = active ? UiTheme.Brush(UiTheme.PrimaryContainer) : Brushes.Transparent;
        }
        favorite = IconButton("heart", "Alternar favorito", async () => { await _music.ToggleFavoriteAsync(track); UpdateFavorite(); Animate(favorite!); }); UpdateFavorite();
        Put(metadata, favorite, 0, 1); Put(panel, metadata, 2, 0);
        var seek = new Grid { RowDefinitions = new("Auto,Auto") };
        _detailSeek = new Slider { Minimum = 0, Maximum = 1, Margin = UiTheme.Zero, MinHeight = UiTheme.TouchSize,
            Foreground = UiTheme.Brush(Accent), Background = UiTheme.Brush(UiTheme.Outline) };
        _detailSeek.AddHandler(PointerPressedEvent, (_, _) => _seeking = true, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _detailSeek.AddHandler(PointerReleasedEvent, (_, _) => { _seeking = false; _music.Player.Seek(_detailSeek.Value); }, Avalonia.Interactivity.RoutingStrategies.Bubble, true);
        _detailSeek.KeyUp += (_, _) => _music.Player.Seek(_detailSeek.Value); seek.Children.Add(_detailSeek);
        var times = new Grid { ColumnDefinitions = new("*,Auto") };
        _detailTime = Text("", UiTheme.Caption, Muted); _detailDuration = Text("", UiTheme.Caption, Muted);
        times.Children.Add(_detailTime); Put(times, _detailDuration, 0, 1); Put(seek, times, 1, 0); Put(panel, seek, 3, 0);
        var controls = new Grid { ColumnDefinitions = new("*,*,Auto,*,*"), ColumnSpacing = UiTheme.ProgressRadius };
        Button? shuffle = null;
        shuffle = IconButton("shuffle", "Aleatório", () => { _music.Shuffle = !_music.Shuffle; shuffle!.Content = ToggleIcon("shuffle", _music.Shuffle); return Task.CompletedTask; }, _music.Shuffle ? Accent : Muted); shuffle.Content = ToggleIcon("shuffle", _music.Shuffle); shuffle.HorizontalAlignment = HorizontalAlignment.Center; controls.Children.Add(shuffle);
        var previous = IconButton("previous", "Anterior", _music.PreviousAsync); previous.Content = Icons.Make("previous", UiTheme.TextPrimary, UiTheme.TransportIconSize); previous.HorizontalAlignment = HorizontalAlignment.Center; Put(controls, previous, 0, 1);
        _detailPlay = IconButton("play", "Reproduzir ou pausar", _music.ToggleAsync); _detailPlay.Classes.Add("primary"); _detailPlay.Classes.Add("largePlay"); _detailPlay.Width = _detailPlay.Height = UiTheme.PlaySize; _detailPlay.CornerRadius = new(UiTheme.PlaySize / 2); Put(controls, _detailPlay, 0, 2);
        var next = IconButton("next", "Próxima", _music.NextAsync); next.Content = Icons.Make("next", UiTheme.TextPrimary, UiTheme.TransportIconSize); next.HorizontalAlignment = HorizontalAlignment.Center; Put(controls, next, 0, 3);
        Button? repeat = null;
        repeat = IconButton("repeat", "Repetir", () => { _music.Repeat = !_music.Repeat; repeat!.Content = ToggleIcon("repeat", _music.Repeat); return Task.CompletedTask; }, _music.Repeat ? Accent : Muted); repeat.Content = ToggleIcon("repeat", _music.Repeat); repeat.HorizontalAlignment = HorizontalAlignment.Center; Put(controls, repeat, 0, 4); Put(panel, controls, 5, 0);
        foreach (var button in new[] { shuffle, previous, next, repeat }) button.Classes.Add("subtle");
        var upcoming = new StackPanel { Spacing = UiTheme.CompactGap };
        upcoming.Children.Add(UiTheme.Text("Próxima", UiTheme.Label, Muted, FontWeight.Medium));
        var currentIndex = _music.Queue.ToList().FindIndex(item => item.Id == track.Id);
        var nextTrack = _music.Queue.Skip(currentIndex + 1).FirstOrDefault();
        var nextTitle = Text(nextTrack is null ? "Nenhuma faixa na sequência" : TrackTextFormatter.Title(nextTrack.Title, nextTrack.Artist), UiTheme.ButtonText);
        nextTitle.TextTrimming = TextTrimming.CharacterEllipsis; upcoming.Children.Add(nextTitle);
        var upcomingBox = UiTheme.Inner(upcoming); Put(panel, upcomingBox, 6, 0);
        panel.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);
        panel.RowDefinitions[4].Height = new GridLength(UiTheme.PlayerBottomWeight, GridUnitType.Star);
        var desktopLayout = new Grid { RowDefinitions = new("Auto,*"), ColumnDefinitions = new("*,*"),
            ColumnSpacing = UiTheme.DesktopPlayerGap, RowSpacing = UiTheme.PageGap, Margin = UiTheme.DesktopPlayerMargin,
            HorizontalAlignment = HorizontalAlignment.Stretch };
        var queuePanel = new Grid { RowDefinitions = new("Auto,*"), RowSpacing = UiTheme.SectionGap };
        var queueHeader = new Grid { ColumnDefinitions = new("*,Auto") };
        var queueHeading = UiTheme.Text("A seguir", UiTheme.SectionTitle, weight: FontWeight.SemiBold); queueHeader.Children.Add(queueHeading);
        Put(queueHeader, ActionButton("Limpar fila", () => { _music.ClearQueue(); return Task.CompletedTask; }), 0, 1);
        queuePanel.Children.Add(queueHeader);
        var queueItems = new ItemsControl { ItemsSource = _music.Queue,
            ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()),
            ItemTemplate = new FuncDataTemplate<Track>((item, _) => item is null ? new Border() : TrackRow(item)) };
        Put(queuePanel, new ScrollViewer { Content = queueItems, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, 1, 0);
        var transport = new Grid { RowDefinitions = new("Auto,Auto"), ColumnDefinitions = new("*,Auto,*"), ColumnSpacing = UiTheme.PageGap,
            Background = UiTheme.Brush(UiTheme.Surface), Margin = UiTheme.Zero };
        var transportFrame = new Border { Child = transport, Background = UiTheme.Brush(UiTheme.Surface),
            BorderBrush = UiTheme.Brush(UiTheme.Outline), BorderThickness = UiTheme.TopBorder, Padding = UiTheme.DesktopTransportPadding };
        var volumeSlider = new Slider { Minimum = 0, Maximum = 1, Value = _music.Player.Volume,
            Width = 100, MinHeight = UiTheme.TouchSize, SmallChange = .05, LargeChange = .1,
            IsVisible = false, Foreground = UiTheme.Brush(UiTheme.Primary), Background = UiTheme.Brush(UiTheme.Outline) };
        Avalonia.Automation.AutomationProperties.SetName(volumeSlider, "Volume");
        volumeSlider.ValueChanged += (_, _) => _music.Player.Volume = volumeSlider.Value;
        Button? volumeButton = null;
        var volumePinned = false;
        volumeButton = IconButton("volume", "Ajustar volume", () => { volumePinned = !volumePinned; volumeSlider.IsVisible = volumePinned; return Task.CompletedTask; });
        var volumeGroup = new StackPanel { Orientation = Orientation.Horizontal };
        volumeGroup.Children.Add(volumeSlider); volumeGroup.Children.Add(volumeButton);
        volumeGroup.PointerEntered += (_, _) => volumeSlider.IsVisible = true;
        volumeGroup.PointerExited += (_, _) => { if (!volumePinned && !volumeSlider.IsKeyboardFocusWithin) volumeSlider.IsVisible = false; };
        var audioTools = new StackPanel { Orientation = Orientation.Horizontal };
        audioTools.Children.Add(volumeGroup);
        audioTools.Children.Add(IconButton("more", "Informações do áudio", () => { ShowAudioInfo(); return Task.CompletedTask; }));
        volumeButton.Classes.Add("subtle"); ((Button)audioTools.Children[1]).Classes.Add("subtle");
        var artworkSlot = new Grid();
        bool? wideLayout = null;
        void ArrangeTransport()
        {
            if (wideLayout != true || transport.Bounds.Width <= 0) return;
            var text = new FormattedText(title.Text ?? "", System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, new Typeface(title.FontFamily, title.FontStyle, title.FontWeight), title.FontSize, title.Foreground);
            var controlsWidth = controls.Bounds.Width;
            var ideal = (transport.Bounds.Width - controlsWidth) / 2 - transport.ColumnSpacing;
            var required = text.Width + favorite!.Bounds.Width + UiTheme.CompactGap;
            var left = Math.Clamp(Math.Max(ideal, required), 0, Math.Max(0,
                transport.Bounds.Width - controlsWidth - UiTheme.PageGap));
            if (Math.Abs(transport.ColumnDefinitions[0].Width.Value - left) > .5)
                transport.ColumnDefinitions[0].Width = new GridLength(left);
        }
        var host = new Grid { RowDefinitions = new("*,Auto") };
        void ArrangePlayer()
        {
            var wide = host.Bounds.Width >= UiTheme.DesktopWidth;
            if (wideLayout != wide)
            {
                panel.Children.Clear(); artworkSlot.Children.Clear(); desktopLayout.Children.Clear(); transport.Children.Clear(); host.Children.Clear();
                header.Children.Remove(audioTools);
                controls.Children.Remove(audioTools);
                if (wide)
                {
                    desktopLayout.VerticalAlignment = VerticalAlignment.Stretch;
                    art.VerticalAlignment = VerticalAlignment.Center; queuePanel.VerticalAlignment = VerticalAlignment.Stretch;
                    title.TextWrapping = TextWrapping.NoWrap; title.MaxLines = 1; title.TextTrimming = TextTrimming.CharacterEllipsis;
                    title.FontSize = UiTheme.Body; artist.FontSize = UiTheme.Caption;
                    labels.Children.Remove(title);
                    title.TextTrimming = TextTrimming.None; title.Width = double.NaN;
                    labels.Children.Insert(0, new ScrollingTitle(title));
                    _detailPlay!.Width = _detailPlay.Height = UiTheme.DesktopPlaySize; _detailPlay.CornerRadius = new(UiTheme.DesktopPlaySize / 2);
                    transport.ColumnDefinitions = new("*,Auto,*");
                    controls.ColumnDefinitions = new("Auto,Auto,Auto,Auto,Auto,Auto");
                    Put(controls, audioTools, 0, 5);
                    Put(desktopLayout, header, 0, 0); Grid.SetColumnSpan(header, 2);
                    artworkSlot.Children.Add(art);
                    Put(desktopLayout, artworkSlot, 1, 0); Put(desktopLayout, queuePanel, 1, 1);
                    Put(transport, metadata, 0, 0); metadata.VerticalAlignment = VerticalAlignment.Center;
                    Put(transport, controls, 0, 1);
                    Put(transport, seek, 1, 0); Grid.SetColumnSpan(seek, 3);
                    Put(host, desktopLayout, 0, 0); Put(host, transportFrame, 1, 0);
                }
                else
                {
                    art.VerticalAlignment = VerticalAlignment.Center;
                    Put(header, audioTools, 0, 3);
                    Grid.SetRow(audioTools, 0);
                    controls.ColumnDefinitions = new("*,*,Auto,*,*");
                    if (title.Parent is ScrollingTitle scrolling)
                    {
                        labels.Children.Remove(scrolling); scrolling.Children.Remove(title);
                        title.RenderTransform = null; title.Width = double.NaN; labels.Children.Insert(0, title);
                    }
                    title.TextWrapping = TextWrapping.Wrap; title.MaxLines = 2; title.FontSize = UiTheme.DialogTitle; artist.FontSize = UiTheme.Body;
                    _detailPlay!.Width = _detailPlay.Height = UiTheme.PlaySize; _detailPlay.CornerRadius = new(UiTheme.PlaySize / 2);
                    Grid.SetColumnSpan(header, 1); Grid.SetColumnSpan(seek, 1);
                    Put(panel, header, 0, 0); Put(panel, art, 1, 0); Put(panel, metadata, 2, 0);
                    Put(panel, seek, 3, 0); Put(panel, controls, 5, 0); Put(panel, upcomingBox, 6, 0);
                    host.Children.Add(panel);
                }
                wideLayout = wide;
            }
            var width = wide ? Math.Min(UiTheme.DesktopArtworkMax, Math.Max(UiTheme.ThumbnailSize,
                (host.Bounds.Width
                - UiTheme.DesktopPlayerMargin.Left - UiTheme.DesktopPlayerMargin.Right - UiTheme.DesktopPlayerGap) / 2))
                : Math.Min(UiTheme.PlayerMaxWidth, host.Bounds.Width - UiTheme.PageMargin.Left - UiTheme.PageMargin.Right);
            var height = host.Bounds.Height - (wide ? UiTheme.DesktopPlayerChromeHeight : UiTheme.PlayerChromeHeight);
            height = Math.Max(UiTheme.ThumbnailSize, height);
            art.Width = art.Height = Math.Max(UiTheme.ThumbnailSize, Math.Min(width, height));
            if (wide)
            {
                desktopLayout.ColumnDefinitions[0].Width = new GridLength(width);
            }
        }
        // Detach initial mobile composition before responsive host takes ownership.
        panel.Children.Clear();
        host.SizeChanged += (_, _) => ArrangePlayer();
        transport.LayoutUpdated += (_, _) => ArrangeTransport();
        _playerPage = new Border { Background = UiTheme.Brush(UiTheme.Background), Child = host };
        Grid.SetColumn(_playerPage, 1); Grid.SetRowSpan(_playerPage, 3); _root.Children.Add(_playerPage);
        _main.IsVisible = false; _mini.IsVisible = false; _bottom.IsVisible = false;
        Animate(_playerPage); RefreshPlayback();
    }
    private void ClosePlayer()
    {
        if (_playerPage is not null) _root.Children.Remove(_playerPage);
        _playerPage = null; _showingPlayer = false; _listFingerprint = null; _main.IsVisible = true; _bottom.IsVisible = Bounds.Width < UiTheme.DesktopWidth;
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
        control.Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(UiTheme.StateTransitionMilliseconds) } };
        Dispatcher.UIThread.Post(() => control.Opacity = 1, DispatcherPriority.Render);
    }
    private void RefreshPlayback()
    {
        UpdateRows(_music.Pending ?? _music.Current);
        var icon = _music.LoadingAudio ? "close" : _music.Player.IsPlaying ? "pause" : "play";
        _play.Content = Icons.Make(icon, UiTheme.OnPrimary);
        if (_detailPlay is not null) _detailPlay.Content = Icons.Make(icon, UiTheme.OnPrimary, UiTheme.TransportIconSize);
        var duration = _music.Player.Duration; if (duration <= 0) duration = _music.Current?.DurationSeconds ?? 0;
        _progress.Maximum = Math.Max(1, duration); _progress.Value = Math.Min(_music.Position, _progress.Maximum);
        if (_detailSeek is not null)
        {
            _detailSeek.Maximum = Math.Max(1, duration); _detailSeek.IsEnabled = duration > 0 && !_music.LoadingAudio;
            if (!_seeking) _detailSeek.Value = Math.Min(_music.Position, _detailSeek.Maximum);
        }
        if (_detailTime is not null) _detailTime.Text = Time(_music.Position);
        if (_detailDuration is not null) _detailDuration.Text = Time(_music.Current?.DurationSeconds > 0 ? _music.Current.DurationSeconds : duration);
    }
    private void ShowSettings()
    {
        var panel = new StackPanel { Spacing = UiTheme.ControlRadius };
        var session = System.IO.File.Exists(CookiesPath);
        panel.Children.Add(Text("YouTube", UiTheme.Body));
        var note = Text(session ? "Sessão importada neste aparelho." : "Sessão não importada. Faixas públicas podem tocar sem login; o YouTube pode pedir verificação.", UiTheme.Label, Muted); note.TextWrapping = TextWrapping.Wrap; panel.Children.Add(note);
        panel.Children.Add(ActionButton("Importar cookies de sessão", ImportCookiesAsync));
        if (session) panel.Children.Add(ActionButton("Remover sessão", () => { System.IO.File.Delete(CookiesPath); AppServices.InvalidateCatalog(); ShowSettings(); return Task.CompletedTask; }));
        panel.Children.Add(ActionButton("Diagnóstico de reprodução", () => { ShowDiagnostics(); return Task.CompletedTask; }));
        panel.Children.Add(ActionButton("Laboratório experimental", () => { ShowExperimental(); return Task.CompletedTask; }));
        ShowSheet("Configurações", panel);
    }
    private string CookiesPath => System.IO.Path.Combine(_music.DataDirectory, "youtube-cookies.txt");
    private CancellationTokenSource? _download;
    private void ShowExperimental()
    {
        var panel = new StackPanel { Spacing = UiTheme.CompactGap };
        var note = Text("Branch de testes. Links de serviços suportados pelo yt-dlp e playlists: até 100 entradas. Downloads: somente áudio original HTTPS direto, sem conversão, até 1 GB.", UiTheme.Caption, Muted);
        note.TextWrapping = TextWrapping.Wrap; panel.Children.Add(note);
        var query = new TextBox { PlaceholderText = "Artista, música ou link de playlist/serviço", Text = _search.Text };
        panel.Children.Add(query);
        foreach (var (label, prefix) in new[] { ("Buscar YouTube", "ytsearch30:"), ("Buscar SoundCloud", "scsearch30:"), ("Abrir link / playlist", "") })
            panel.Children.Add(ActionButton(label, async () =>
            {
                var text = query.Text?.Trim() ?? "";
                if (text.Length == 0) return;
                if (prefix.Length == 0 && (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")))
                    throw new ArgumentException("Informe link HTTP ou HTTPS.");
                CloseSheet(); _page = "explore"; await _music.SearchAsync(prefix + text);
            }));
        panel.Children.Add(ActionButton("Adicionar resultados à fila", () => { _music.EnqueueResults(); return Task.CompletedTask; }));
        panel.Children.Add(ActionButton("Ordenar fila por título", () => { _music.ReorderQueue(false); return Task.CompletedTask; }));
        panel.Children.Add(ActionButton("Embaralhar fila agora", () => { _music.ReorderQueue(true); return Task.CompletedTask; }));
        panel.Children.Add(ActionButton("Limpar fila (manter atual)", () => { _music.ClearQueue(); return Task.CompletedTask; }));
        panel.Children.Add(ActionButton(_music.RepeatQueue ? "Desativar repetir fila" : "Repetir fila inteira", () => { _music.RepeatQueue = !_music.RepeatQueue; ShowExperimental(); return Task.CompletedTask; }));
        foreach (var seconds in new[] { -10, 10, 30 })
            panel.Children.Add(ActionButton($"Pular {seconds:+0;-0} segundos", () => { _music.Player.Seek(Math.Max(0, _music.Position + seconds)); return Task.CompletedTask; }));
        foreach (var minutes in new[] { 15, 30, 60 })
            panel.Children.Add(ActionButton($"Sleep timer: {minutes} min", () => { _music.SleepAt = DateTime.UtcNow.AddMinutes(minutes); _music.SetStatus($"Pausar em {minutes} minutos."); return Task.CompletedTask; }));
        panel.Children.Add(ActionButton("Cancelar sleep timer", () => { _music.SleepAt = null; return Task.CompletedTask; }));
        panel.Children.Add(ActionButton("Baixar faixa atual (formato original)", DownloadCurrentAsync));
        panel.Children.Add(ActionButton("Cancelar download", () => { _download?.Cancel(); return Task.CompletedTask; }));
        panel.Children.Add(ActionButton("Histórico desta sessão", () =>
        {
            var history = new StackPanel { Spacing = UiTheme.CompactGap };
            foreach (var track in _music.History.ToArray()) history.Children.Add(ActionButton(track.Artist + " — " + track.Title, async () => { CloseSheet(); await _music.PlayAsync(track); }));
            ShowSheet("Histórico", history); return Task.CompletedTask;
        }));
        ShowSheet("Laboratório experimental", panel);
    }
    private async Task DownloadCurrentAsync()
    {
        if (_download is not null) throw new InvalidOperationException("Já existe download em andamento.");
        var track = _music.Current ?? throw new InvalidOperationException("Reproduza uma faixa primeiro.");
        var source = _music.CurrentSource ?? throw new InvalidOperationException("Fonte ainda indisponível.");
        if (track.IsLocal) throw new InvalidOperationException("Faixa já é local.");
        using var request = new CancellationTokenSource(TimeSpan.FromMinutes(30)); _download = request;
        try
        {
            var path = await AudioDownload.SaveAsync(source, System.IO.Path.Combine(_music.DataDirectory, "downloads"), new Progress<string>(_music.SetStatus), request.Token);
            var local = track with { Id = Guid.NewGuid().ToString("N"), Source = path, IsLocal = true };
            await _music.ToggleFavoriteAsync(local); _music.SetStatus("Download salvo na biblioteca local.");
        }
        catch (OperationCanceledException) { _music.SetStatus("Download cancelado ou tempo esgotado."); }
        finally { _download = null; }
    }
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
        var panel = new StackPanel { Spacing = UiTheme.SectionGap };
        var text = PlaybackDiagnostics.Snapshot(); if (string.IsNullOrWhiteSpace(text)) text = "Nenhuma tentativa registrada. Tente reproduzir uma faixa primeiro.";
        panel.Children.Add(UiTheme.Inner(new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontFamily = UiTheme.Inter, FontSize = UiTheme.Label, Foreground = UiTheme.Brush(Muted) }));
        panel.Children.Add(ActionButton("Copiar diagnóstico", async () => { var clipboard = TopLevel.GetTopLevel(this)?.Clipboard; if (clipboard is not null) await clipboard.SetTextAsync(PlaybackDiagnostics.Snapshot()); }));
        ShowSheet("Diagnóstico", panel);
    }
    private void ShowAudioInfo()
    {
        var source = _music.CurrentSource;
        var panel = new StackPanel { Spacing = UiTheme.SectionGap };
        panel.Children.Add(Text(TrackTextFormatter.Title(source?.Title ?? _music.Current?.Title ?? "", source?.Artist ?? _music.Current?.Artist), UiTheme.SectionTitle));
        void Detail(string label, string? value)
        {
            var row = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = UiTheme.SectionGap };
            row.Children.Add(Text(label, UiTheme.Body, Muted));
            Put(row, Text(string.IsNullOrWhiteSpace(value) ? "Não informado" : value, UiTheme.Body), 0, 1);
            panel.Children.Add(UiTheme.Inner(row));
        }
        Detail(_music.Current?.IsLocal == true ? "Bitrate médio do áudio" : "Bitrate da fonte", source?.BitrateKbps is > 0 ? $"{source.BitrateKbps:0.#} kb/s" : null);
        Detail("Artista", source?.Artist);
        if (_music.Current?.Album is { } album) Detail("Álbum", album);
        if (_music.Current?.TrackNumber is { } number) Detail("Faixa", number);
        if (_music.Current?.Date is { } date) Detail("Data", date);
        Detail("Codec", source?.Codec);
        Detail("Contêiner", source?.Container);
        Detail("Amostragem", source?.SampleRateHz is > 0 ? $"{source.SampleRateHz / 1000:0.#} kHz" : null);
        Detail("Canais", source?.Channels?.ToString("0"));
        if (source?.BitDepth is > 0) Detail("Profundidade", $"{source.BitDepth} bits");
        if (_music.Current?.IsLocal != true) Detail("Formato", source?.Format);
        Detail("Volume", $"{_music.Player.Volume:P0}");
        var note = Text(_music.Current?.IsLocal == true
            ? "Dados lidos do arquivo. Em FLAC, bitrate médio calculado pelo tamanho do áudio e número de amostras, sem incluir metadados."
            : "Dados fornecidos pela fonte; bitrate pode ser variável. Valores ausentes não são estimados.", UiTheme.Caption, Muted);
        note.TextWrapping = TextWrapping.Wrap; panel.Children.Add(note);
        panel.Children.Add(ActionButton("Abrir diagnóstico", () => { ShowDiagnostics(); return Task.CompletedTask; }));
        ShowSheet("Informações do áudio", panel);
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
        if (!_showingPlayer) { _detailTrack = null; _detailSeek = null; _detailPlay = null; _detailTime = null; _detailDuration = null; _seeking = false; }
    }
    private static string Time(double seconds) => TrackTextFormatter.Duration(seconds);
    private static Control ToggleIcon(string name, bool active)
    {
        var panel = new Grid { Width = UiTheme.IconSize, Height = UiTheme.ToggleIconHeight };
        var icon = new Grid(); icon.Children.Add(Icons.Make(name, active ? Accent : Muted));
        if (name == "repeat" && active)
        {
            var badge = UiTheme.Text("1", UiTheme.Label, Accent, FontWeight.SemiBold);
            badge.HorizontalAlignment = HorizontalAlignment.Center; icon.Children.Add(badge);
        }
        panel.Children.Add(icon);
        panel.Children.Add(new Border { Width = UiTheme.ProgressRadius, Height = UiTheme.ProgressRadius,
            CornerRadius = new(UiTheme.ProgressRadius), Background = active ? UiTheme.Brush(Accent) : Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom });
        return panel;
    }
    private static TextBlock Text(string text, double size, string color = UiTheme.TextPrimary) => UiTheme.Text(text, size, color);
    private Button ActionButton(string text, Func<Task> action)
    {
        var button = UiTheme.OutlinedButton(text);
        BindAction(button, action); return button;
    }
    private void BindAction(Button button, Func<Task> action)
    {
        button.Click += async (_, _) => { try { await action(); } catch (Exception ex) { PlaybackDiagnostics.Write("Interface", ex.Message); _music.SetStatus(ex.Message); ShowDiagnostics(); } };
    }
    private Button IconButton(string icon, string label, Func<Task> action, string color = Accent)
    {
        var button = UiTheme.RoundIconButton(icon, label, color); button.VerticalAlignment = VerticalAlignment.Center;
        BindAction(button, action); return button;
    }
    private static void Put(Grid grid, Control control, int row, int column) { Grid.SetRow(control, row); Grid.SetColumn(control, column); grid.Children.Add(control); }
}
