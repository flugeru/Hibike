using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Hibiki.Core.Models;

namespace Hibiki.App.Views;

public sealed class MainView : UserControl
{
    private readonly MusicController _music;
    private readonly Grid _root = new() { ColumnDefinitions = new("224,*"), RowDefinitions = new("*,Auto,Auto") };
    private readonly Border _sidebar;
    private readonly Border _main;
    private readonly Grid _body = new() { ColumnDefinitions = new("*,300") };
    private readonly Border _rail;
    private readonly StackPanel _mobileNav = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 12 };
    private readonly TextBox _search = new() { Watermark = "Música, artista ou link do YouTube", MinWidth = 0 };
    private readonly Button _searchButton;
    private readonly Button _cancelSearch;
    private readonly TextBlock _status = Text("", 12, "#A9A1B8");
    private readonly TextBlock _section = Text("Comece pelo que você gosta", 22);
    private readonly TextBlock _count = Text("", 12, "#A9A1B8");
    private readonly StackPanel _tracks = new() { Spacing = 8 };
    private readonly StackPanel _queue = new() { Spacing = 12 };
    private readonly Border _hero;
    private readonly StackPanel _genres;
    private readonly TextBlock _heroTitle;
    private readonly Grid _mobileBrand;
    private readonly ContentControl _nowArt = new();
    private readonly TextBlock _nowTitle = Text("Seu som começa aqui", 14);
    private readonly TextBlock _nowArtist = Text("Escolha uma faixa para ouvir", 12, "#A9A1B8");
    private readonly Button _play;
    private readonly Button _favorite;
    private readonly Button _shuffle;
    private readonly Button _repeat;
    private readonly Slider _seek = new() { Minimum = 0, Maximum = 1, Value = 0, IsEnabled = false };
    private readonly TextBlock _position = Text("0:00", 11, "#A9A1B8");
    private readonly TextBlock _duration = Text("0:00", 11, "#A9A1B8");
    private readonly List<Button> _navButtons = [];
    private Track? _shownTrack;
    private string _page = "explore";
    private bool _seeking;
    private bool _refreshing;

    public MainView(MusicController music)
    {
        _music = music;
        Background = Brush.Parse("#101014");
        _sidebar = BuildSidebar();
        _root.Children.Add(_sidebar); Grid.SetRowSpan(_sidebar, 3);

        _main = new Border { Padding = new Thickness(28, 26, 28, 8) };
        Grid.SetColumn(_main, 1); _root.Children.Add(_main);
        var mainGrid = new Grid { RowDefinitions = new("Auto,Auto,*,Auto") };
        _main.Child = mainGrid;
        _mobileBrand = Brand(); _mobileBrand.Margin = new Thickness(0, 0, 0, 20);
        mainGrid.Children.Add(_mobileBrand);

        var searchRow = new Grid { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = 8, Margin = new Thickness(0, 0, 0, 22) };
        searchRow.Children.Add(_search);
        _searchButton = IconButton("search", "Buscar", async () => await SearchAsync());
        _searchButton.Classes.Remove("subtle"); _searchButton.Classes.Add("primary");
        _searchButton.Content = Icons.Make("search", "#211337", 20);
        Put(searchRow, _searchButton, 0, 1);
        _cancelSearch = IconButton("close", "Cancelar busca", () => { _music.CancelSearch(); return Task.CompletedTask; });
        Put(searchRow, _cancelSearch, 0, 2);
        _search.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await SearchAsync(); } };
        Put(mainGrid, searchRow, 1, 0);
        Put(mainGrid, _body, 2, 0);

        var scroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var feed = new StackPanel { Spacing = 20, Margin = new Thickness(0, 0, 0, 16) };
        scroll.Content = feed; _body.Children.Add(scroll);
        _heroTitle = Text("Dê play no\nseu universo.", 36); _heroTitle.FontWeight = FontWeight.Bold;
        _hero = BuildHero(); feed.Children.Add(_hero);
        _genres = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var (label, query) in new[] { ("Lo-fi", "lofi chill music"), ("J-pop", "jpop music"), ("Anime", "anime opening music"), ("Rock", "rock music"), ("Eletrônica", "electronic music") })
        {
            var chip = ActionButton(label, async () => { _search.Text = query; await SearchAsync(); });
            chip.Classes.Add("chip"); _genres.Children.Add(chip);
        }
        feed.Children.Add(new ScrollViewer { Content = _genres, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var heading = new Grid { ColumnDefinitions = new("*,Auto") };
        heading.Children.Add(_section); Put(heading, _count, 0, 1); _count.VerticalAlignment = VerticalAlignment.Center;
        feed.Children.Add(heading); feed.Children.Add(_tracks);

        _rail = BuildQueueRail(); Put(_body, _rail, 0, 1);
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Margin = new Thickness(0, 10, 0, 8); Put(mainGrid, _status, 3, 0);

        _play = IconButton("play", "Reproduzir ou pausar", _music.ToggleAsync, 24);
        _play.Classes.Remove("subtle"); _play.Classes.Add("primary"); _play.Width = _play.Height = 50; _play.CornerRadius = new CornerRadius(25);
        _favorite = IconButton("heart", "Favoritar faixa atual", async () => { if (_music.Current is { } track) await _music.ToggleFavoriteAsync(track); });
        _shuffle = IconButton("shuffle", "Alternar ordem aleatória", () => { _music.Shuffle = !_music.Shuffle; Refresh(); return Task.CompletedTask; });
        _repeat = IconButton("repeat", "Repetir faixa atual", () => { _music.Repeat = !_music.Repeat; Refresh(); return Task.CompletedTask; });
        var player = BuildPlayer(); Put(_root, player, 1, 1);
        foreach (var (key, label, icon) in new[] { ("explore", "Explorar", "search"), ("library", "Biblioteca", "heart"), ("queue", "Fila", "queue") })
            _mobileNav.Children.Add(NavButton(key, label, icon));
        var bottom = new Border { Child = _mobileNav, Padding = new Thickness(8, 8, 8, 10), Background = Brush.Parse("#15131B") };
        Put(_root, bottom, 2, 1);
        Content = _root;
        SizeChanged += (_, _) =>
        {
            var compact = Bounds.Width < 850;
            var rail = Bounds.Width >= 1180;
            _root.ColumnDefinitions[0].Width = new GridLength(compact ? 0 : 224);
            _sidebar.IsVisible = !compact; bottom.IsVisible = compact; _mobileBrand.IsVisible = compact;
            _body.ColumnDefinitions[1].Width = new GridLength(rail ? 300 : 0);
            _rail.IsVisible = rail;
            _main.Padding = compact ? new Thickness(18, 20, 18, 4) : new Thickness(30, 28, 30, 8);
            _heroTitle.FontSize = compact ? 30 : 36;
        };
        _music.Changed += Refresh; _music.Tick += RefreshPlayback;
        AttachedToVisualTree += async (_, _) => await _music.InitializeAsync();
        DetachedFromVisualTree += (_, _) => _music.StopObserving();
        Refresh();
    }

    private Border BuildSidebar()
    {
        var panel = new StackPanel { Spacing = 8, Margin = new Thickness(20, 30, 16, 20) };
        var brand = Brand(); brand.Margin = new Thickness(6, 0, 0, 40); panel.Children.Add(brand);
        panel.Children.Add(Text("SEU ESPAÇO", 10, "#827A93"));
        panel.Children.Add(NavButton("explore", "Explorar", "search"));
        panel.Children.Add(NavButton("library", "Sua biblioteca", "heart"));
        panel.Children.Add(NavButton("queue", "Fila de reprodução", "queue"));
        var import = ActionButton("Abrir áudio local", OpenLocalAsync); import.Margin = new Thickness(0, 22, 0, 0); panel.Children.Add(import);
        var bottom = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(26, 20) };
        bottom.Children.Add(Text("響  HIBIKI", 12, "#BDA2FF"));
        bottom.Children.Add(Text("Feito por Flugel", 11, "#827A93"));
        bottom.Children.Add(Text("Sua música. Seu ritmo.", 11, "#827A93"));
        var grid = new Grid(); grid.Children.Add(panel); grid.Children.Add(bottom);
        return new Border { Background = Brush.Parse("#15131B"), BorderBrush = Brush.Parse("#25212F"), BorderThickness = new Thickness(0, 0, 1, 0), Child = grid };
    }
    private static Grid Brand()
    {
        var grid = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 12 };
        grid.Children.Add(new Border { Background = Brush.Parse("#30243F"), CornerRadius = new CornerRadius(13), Padding = new Thickness(10), Child = Icons.Make("wave", "#BDA2FF", 24) });
        var name = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
        var title = Text("hibiki", 25); title.FontWeight = FontWeight.Bold; name.Children.Add(title);
        name.Children.Add(Text("by Flugel", 10, "#A9A1B8")); Put(grid, name, 0, 1);
        return grid;
    }
    private Border BuildHero()
    {
        var text = new StackPanel { Spacing = 12, Margin = new Thickness(24) };
        var eyebrow = Text("SINTA CADA NOTA", 10, "#D8C4FF"); eyebrow.LetterSpacing = 2;
        text.Children.Add(eyebrow); text.Children.Add(_heroTitle);
        var subtitle = Text("Encontre o som que combina com você.", 13, "#D1C5E4"); subtitle.TextWrapping = TextWrapping.Wrap; text.Children.Add(subtitle);
        var open = ActionButton("Abrir minha música", OpenLocalAsync); open.Classes.Add("primary"); open.HorizontalAlignment = HorizontalAlignment.Left; text.Children.Add(open);
        var grid = new Grid { ColumnDefinitions = new("*,72") };
        grid.Children.Add(text);
        var waves = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0.6, Margin = new Thickness(0, 0, 14, 0) };
        foreach (var height in new[] { 35d, 72d, 118d, 85d, 48d }) waves.Children.Add(new Border { Width = 6, Height = height, CornerRadius = new CornerRadius(3), Background = Brush.Parse("#BDA2FF"), VerticalAlignment = VerticalAlignment.Center });
        Put(grid, waves, 0, 1);
        return new Border
        {
            CornerRadius = new CornerRadius(24), ClipToBounds = true, Child = grid,
            Background = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = [new GradientStop(Color.Parse("#48305F"), 0), new GradientStop(Color.Parse("#241D36"), 1)] }
        };
    }
    private Border BuildQueueRail()
    {
        var stack = new StackPanel { Spacing = 18 };
        var title = Text("A seguir", 20); title.FontWeight = FontWeight.SemiBold; stack.Children.Add(title);
        stack.Children.Add(Text("O próximo capítulo do seu som.", 12, "#A9A1B8")); stack.Children.Add(_queue);
        var clear = ActionButton("Limpar fila", () => { _music.ClearQueue(); return Task.CompletedTask; }); clear.Classes.Add("subtle"); stack.Children.Add(clear);
        return new Border { Margin = new Thickness(24, 0, 0, 0), Padding = new Thickness(20), Background = Brush.Parse("#19161F"), CornerRadius = new CornerRadius(22),
            Child = new ScrollViewer { Content = stack, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } };
    }
    private Border BuildPlayer()
    {
        var grid = new Grid { RowDefinitions = new("Auto,Auto,Auto"), RowSpacing = 6, Margin = new Thickness(18, 12, 18, 10) };
        var song = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 12 };
        _nowArt.Content = new Artwork(null, 44); song.Children.Add(_nowArt);
        var metadata = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        _nowTitle.TextTrimming = TextTrimming.CharacterEllipsis; _nowArtist.TextTrimming = TextTrimming.CharacterEllipsis;
        metadata.Children.Add(_nowTitle); metadata.Children.Add(_nowArtist); Put(song, metadata, 0, 1); Put(song, _favorite, 0, 2);
        grid.Children.Add(song);
        var timeline = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 10 };
        _position.VerticalAlignment = _duration.VerticalAlignment = VerticalAlignment.Center;
        timeline.Children.Add(_position); Put(timeline, _seek, 0, 1); Put(timeline, _duration, 0, 2); Put(grid, timeline, 1, 0);
        _seek.AddHandler(PointerPressedEvent, (_, _) => _seeking = true, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _seek.AddHandler(PointerReleasedEvent, (_, _) => { _seeking = false; _music.Player.Seek(_seek.Value); }, Avalonia.Interactivity.RoutingStrategies.Bubble, true);
        _seek.KeyUp += (_, _) => _music.Player.Seek(_seek.Value);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 12 };
        controls.Children.Add(_shuffle); controls.Children.Add(IconButton("previous", "Faixa anterior", _music.PreviousAsync));
        controls.Children.Add(_play); controls.Children.Add(IconButton("next", "Próxima faixa", _music.NextAsync)); controls.Children.Add(_repeat);
        Put(grid, controls, 2, 0);
        return new Border { Background = Brush.Parse("#1B1723"), BorderBrush = Brush.Parse("#33283F"), BorderThickness = new Thickness(0, 1, 0, 0), Child = grid };
    }
    private Button NavButton(string page, string label, string icon)
    {
        var button = ActionButton("", () => { _page = page; Refresh(); return Task.CompletedTask; });
        button.Tag = page; button.Classes.Add("nav"); button.Content = Inline(icon, label, 13); _navButtons.Add(button);
        return button;
    }
    private async Task SearchAsync()
    {
        _page = "explore"; await _music.SearchAsync(_search.Text ?? "");
    }
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
        _status.Text = _music.Status;
        _cancelSearch.IsVisible = _music.Searching;
        _searchButton.IsEnabled = !_music.Searching;
        foreach (var nav in _navButtons) nav.Classes.Set("active", Equals(nav.Tag, _page));
        _hero.IsVisible = _page == "explore" && _music.Results.Count == 0;
        _genres.IsVisible = _page == "explore";
        _section.Text = _page switch { "library" => "Sua biblioteca", "queue" => "Fila de reprodução", _ => _music.Results.Count > 0 ? "Encontre seu próximo som" : "Comece pelo que você gosta" };
        IReadOnlyList<Track> tracks = _page switch { "library" => _music.Favorites, "queue" => _music.Queue, _ => _music.Results };
        _count.Text = tracks.Count == 0 ? "" : tracks.Count + " faixas";
        _tracks.Children.Clear();
        if (tracks.Count == 0)
        {
            var empty = new StackPanel { Spacing = 14, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(16, 24) };
            empty.Children.Add(Icons.Make(_page == "library" ? "heart" : "wave", "#BDA2FF", 32));
            var text = Text(_page switch { "library" => "As músicas que você ama ficam aqui.", "queue" => "Adicione faixas para continuar ouvindo.", _ => "Busque uma música ou escolha um estilo acima." }, 13, "#A9A1B8");
            text.TextWrapping = TextWrapping.Wrap; text.TextAlignment = TextAlignment.Center; empty.Children.Add(text);
            if (_page == "library") empty.Children.Add(ActionButton("Importar áudio", OpenLocalAsync));
            _tracks.Children.Add(empty);
        }
        else foreach (var track in tracks) _tracks.Children.Add(TrackRow(track));
        _queue.Children.Clear();
        if (_music.Queue.Count == 0) _queue.Children.Add(Text("Sua fila está esperando\npela primeira faixa.", 13, "#827A93"));
        foreach (var track in _music.Queue.Take(8))
        {
            var button = ActionButton("", async () => await _music.PlayAsync(track)); button.Classes.Add("subtle"); button.Padding = new Thickness(0, 6);
            var row = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 10 };
            row.Children.Add(new Artwork(track, 42)); var info = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            var title = Text(track.Title, 12); title.TextTrimming = TextTrimming.CharacterEllipsis; info.Children.Add(title);
            var artist = Text(track.Artist, 11, "#827A93"); artist.TextTrimming = TextTrimming.CharacterEllipsis; info.Children.Add(artist); Put(row, info, 0, 1);
            button.Content = row; button.HorizontalContentAlignment = HorizontalAlignment.Stretch; _queue.Children.Add(button);
        }
        if (!Equals(_shownTrack, _music.Current))
        {
            _shownTrack = _music.Current; _nowArt.Content = new Artwork(_shownTrack, 44);
        }
        _nowTitle.Text = _music.Current?.Title ?? "Seu som começa aqui";
        _nowArtist.Text = _music.LoadingAudio ? "Preparando áudio…" : _music.Current?.Artist ?? "Escolha uma faixa para ouvir";
        _favorite.Content = Icons.Make("heart", _music.Current is { } current && _music.IsFavorite(current) ? "#BDA2FF" : "#A9A1B8");
        _shuffle.Content = Icons.Make("shuffle", _music.Shuffle ? "#BDA2FF" : "#827A93");
        _repeat.Content = Icons.Make("repeat", _music.Repeat ? "#BDA2FF" : "#827A93");
        RefreshPlayback();
    }
    private Control TrackRow(Track track)
    {
        var grid = new Grid { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = 2 };
        var play = ActionButton("", async () => await _music.PlayAsync(track)); play.Classes.Add("subtle"); play.Padding = new Thickness(8); play.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        var row = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 12 };
        row.Children.Add(new Artwork(track, 48));
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 4 };
        var title = Text(track.Title, 13); title.TextTrimming = TextTrimming.CharacterEllipsis; info.Children.Add(title);
        var artist = Text(track.Artist + "  ·  " + track.DurationLabel, 11, "#A9A1B8"); artist.TextTrimming = TextTrimming.CharacterEllipsis; info.Children.Add(artist);
        Put(row, info, 0, 1); play.Content = row; grid.Children.Add(play);
        var favorite = IconButton("heart", "Favoritar " + track.Title, async () => await _music.ToggleFavoriteAsync(track));
        favorite.Content = Icons.Make("heart", _music.IsFavorite(track) ? "#BDA2FF" : "#827A93", 18); Put(grid, favorite, 0, 1);
        var add = IconButton("plus", "Adicionar à fila: " + track.Title, () => { _music.Enqueue(track); return Task.CompletedTask; }, 18); Put(grid, add, 0, 2);
        return new Border { Background = Brush.Parse("#19171F"), CornerRadius = new CornerRadius(14), Child = grid };
    }
    private void RefreshPlayback()
    {
        _play.Content = Icons.Make(_music.LoadingAudio ? "close" : _music.Player.IsPlaying ? "pause" : "play", "#211337", 23);
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            var duration = _music.Player.Duration;
            if (duration <= 0) duration = _music.Current?.DurationSeconds ?? 0;
            _seek.IsEnabled = duration > 0 && !_music.LoadingAudio;
            _seek.Maximum = Math.Max(1, duration);
            if (!_seeking) _seek.Value = Math.Min(_music.Player.Position, _seek.Maximum);
            _position.Text = Time(_music.Player.Position); _duration.Text = Time(duration);
        }
        finally { _refreshing = false; }
    }
    private static string Time(double seconds) => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss");
    private static TextBlock Text(string text, double size, string color = "#F4F0FA") => new() { Text = text, FontSize = size, Foreground = Brush.Parse(color) };
    private Button ActionButton(string text, Func<Task> action)
    {
        var button = new Button { Content = text };
        button.Click += async (_, _) => { try { await action(); } catch (Exception ex) { _music.SetStatus(ex.Message); } };
        return button;
    }
    private Button IconButton(string icon, string label, Func<Task> action, double size = 20)
    {
        var button = ActionButton("", action); button.Content = Icons.Make(icon, size: size); button.Padding = new Thickness(10); button.Classes.Add("subtle");
        button.MinWidth = 44; button.VerticalAlignment = VerticalAlignment.Center;
        Avalonia.Automation.AutomationProperties.SetName(button, label); ToolTip.SetTip(button, label);
        return button;
    }
    private static Control Inline(string icon, string label, double size)
    {
        var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        stack.Children.Add(Icons.Make(icon, size: 19)); stack.Children.Add(Text(label, size)); return stack;
    }
    private static void Put(Grid grid, Control control, int row, int column)
    { Grid.SetRow(control, row); Grid.SetColumn(control, column); grid.Children.Add(control); }
}
