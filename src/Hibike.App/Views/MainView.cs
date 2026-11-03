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
using Avalonia.Styling;
using Avalonia.VisualTree;
using Hibike.Core.Models;
using Hibike.Core.Services;
using Track = Hibike.Core.Models.Track;

namespace Hibike.App.Views;

public sealed partial class MainView : UserControl
{
    private readonly MusicController _music;
    private readonly Grid _root = new() { ColumnDefinitions = new($"{UiTheme.SidebarWidth},*"), RowDefinitions = new("*,Auto,Auto") };
    private readonly Border _sidebar;
    private readonly Border _main;
    private readonly StackPanel _home = new() { Spacing = UiTheme.SectionGap };
    private ScrollViewer _homeScroll = null!;
    private ScrollViewer _trackScroll = null!;
    private Control _listHeading = null!;
    private IReadOnlyList<AccountPlaylist> _accountPlaylists = [];
    private IReadOnlyList<Track> _accountLikes = [];
    private string? _homeFingerprint;
    private bool _accountLoading;
    private readonly TextBlock _pageTitle = Text("Início", UiTheme.PageTitle);
    private readonly StackPanel _listTools = new() { Spacing = UiTheme.CompactGap };
    private readonly TextBox _libraryFilter = UiTheme.Field("Filtrar título ou artista");
    private readonly Border _emptyState = new();
    private string _libraryKind = "Todas";
    private bool _librarySorted;
    private readonly TextBox _search = UiTheme.Field("Música, artista ou link");
    private readonly TextBlock _heading = Text("Resultados", UiTheme.SectionTitle);
    private readonly TextBlock _count = Text("", UiTheme.Label, Muted);
    private readonly ItemsControl _tracks = new() { ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()) };
    private readonly TextBlock _status = Text("", UiTheme.Caption, Muted);
    private readonly Border _statusBox;
    private readonly Border _searchLoading;
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
    private string _page = "home";
    private const string Muted = UiTheme.TextSecondary, Accent = UiTheme.Primary;

    public MainView(MusicController music)
    {
        _music = music; Background = UiTheme.Brush(UiTheme.Background);
        TopLevel.SetAutoSafeAreaPadding(this, true);
        _sidebar = BuildSidebar(); _root.Children.Add(_sidebar); Grid.SetRowSpan(_sidebar, 3);
        _main = new Border { Padding = UiTheme.ContentPadding, HorizontalAlignment = HorizontalAlignment.Stretch }; Put(_root, _main, 0, 1);
        var content = new Grid { RowDefinitions = new("Auto,Auto,Auto,Auto,Auto,*"), RowSpacing = UiTheme.PageGap }; _main.Child = content;
        var header = new Grid { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = UiTheme.CompactGap };
        var brandIcon = _brandIcon = new Image { Source = new Avalonia.Media.Imaging.Bitmap(Avalonia.Platform.AssetLoader.Open(new Uri("avares://Hibike.App/Assets/hibike-desktop.png"))), Width = 36, Height = 36, VerticalAlignment = VerticalAlignment.Center };
        var pageBrand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        _pageTitle.FontWeight = FontWeight.SemiBold; pageBrand.Children.Add(brandIcon); pageBrand.Children.Add(_pageTitle); header.Children.Add(pageBrand);
        var headerImport = IconButton("folder", "Abrir áudio local", OpenLocalAsync); Put(header, headerImport, 0, 1);
        var headerSettings = IconButton("settings", "Conta e configurações", ShowAccountMenuAsync); Put(header, headerSettings, 0, 2);
        var androidMenu = IconButton("menu", "Menu do aplicativo", () => { ToggleTopMenu(); return Task.CompletedTask; });
        androidMenu.IsVisible = OperatingSystem.IsAndroid(); Put(header, androidMenu, 0, 2);
        if (OperatingSystem.IsAndroid()) { brandIcon.IsVisible = true; _pageTitle.Text = "Hibiki"; headerImport.IsVisible = headerSettings.IsVisible = false; }
        pageBrand.VerticalAlignment = VerticalAlignment.Center; _pageTitle.VerticalAlignment = VerticalAlignment.Center;
        content.Children.Add(header);
        var searchRow = new Grid { ColumnDefinitions = new("*") };
        searchRow.Children.Add(_search);
        AddSearchIcon(_search);
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
        Put(heading, headingActions, 0, 1); Put(content, heading, 2, 0); _listHeading = heading;
        _tracks.ItemTemplate = new FuncDataTemplate<Track>((track, _) => track is null ? new Border() : TrackRow(track));
        var trackScroll = UiTheme.FadingScroll(_tracks);
        Put(content, _listTools, 4, 0);
        Put(content, trackScroll, 5, 0);
        _trackScroll = trackScroll;
        _homeScroll = UiTheme.FadingScroll(_home);
        Put(content, _homeScroll, 5, 0);
        Put(content, _emptyState, 5, 0);
        _libraryFilter.TextChanged += (_, _) => { if (_page == "library") Refresh(); };
        var statusRow = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = UiTheme.CompactGap };
        _status.TextWrapping = TextWrapping.Wrap; _status.MaxLines = 2; _status.VerticalAlignment = VerticalAlignment.Center; statusRow.Children.Add(_status);
        if (DeveloperFeatures.Enabled) Put(statusRow, IconButton("info", "Ver diagnóstico", () => { ShowDiagnostics(); return Task.CompletedTask; }), 0, 1);
        _statusBox = new Border { Child = statusRow }; UiTheme.StateBanner(_statusBox, UiTheme.Warning); Put(content, _statusBox, 3, 0);
        _play = IconButton("play", "Reproduzir, pausar ou cancelar", _music.ToggleAsync, UiTheme.OnPrimary); _play.Classes.Add("primary"); _play.Classes.Add("miniPlay");
        _mini = BuildMiniPlayer(); Put(_root, _mini, 1, 1);
        InitializeWorkspace(content);
        _libraryGroupScroll.Content = _libraryGroups; Put(content, _libraryGroupScroll, 5, 0);
        var searchLoading = new StackPanel { Spacing = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        searchLoading.Children.Add(LoadingRing(40, UiTheme.Primary));
        searchLoading.Children.Add(Text("Buscando seu som", UiTheme.ButtonText));
        var cancelLoading = ActionButton("Cancelar busca", () => { _music.CancelSearch(); return Task.CompletedTask; });
        cancelLoading.Classes.Add("subtle"); searchLoading.Children.Add(cancelLoading);
        _searchLoading = new Border { Background = UiTheme.Brush(UiTheme.Background), Child = searchLoading, IsVisible = false };
        Avalonia.Automation.AutomationProperties.SetName(_searchLoading, "Busca em andamento");
        Put(content, _searchLoading, 5, 0);
        InitializeTopMenu(content);
        var navGrid = new Grid { ColumnDefinitions = new("*,*,*,*"), Height = OperatingSystem.IsAndroid() ? 60 : UiTheme.NavHeight, Background = UiTheme.Brush(UiTheme.Surface) };
        var tabs = new[] { ("home", "Início", "home"), ("explore", "Buscar", "search"), ("library", "Biblioteca", "library"), ("queue", "Fila", "queue") };
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
            headerImport.IsVisible = headerSettings.IsVisible = false;
            androidMenu.IsVisible = compact;
            brandIcon.IsVisible = _page == "home";
            if (_page == "home") _pageTitle.Text = compact ? "Hibiki" : "Hibike";
            if (!compact && !OperatingSystem.IsAndroid()) CloseTopMenu();
            _main.IsVisible = !_showingPlayer;
            _bottom.IsVisible = compact && !_showingPlayer;
            _main.Padding = compact ? new Thickness(12, 8, 12, 4) : new Thickness(32, 24, 32, 12);
            _home.Spacing = compact ? 8 : UiTheme.SectionGap;
            if (_page == "home") RefreshHome();
            RefreshWorkspace();
            // Title and two 48dp actions fit at 360dp; no artificial second toolbar row.
        };
        if (OperatingSystem.IsAndroid() && AppServices.RequestDisplayFrame is { } requestFrame)
            SynchronizeHomeInertia(requestFrame);
        AppServices.BackHandler = HandleBack; AppServices.OpenPlayerView = ShowPlayer;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = HandleBack(); }
            else if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            { _search.Focus(); _search.SelectAll(); e.Handled = true; }
        };
        AttachedToVisualTree += async (_, _) =>
        {
            _music.Changed -= Refresh; _music.Tick -= RefreshPlayback; _music.Tick -= RefreshDock;
            _music.Changed += Refresh; _music.Tick += RefreshPlayback; _music.Tick += RefreshDock;
            _music.StartObserving(); _listFingerprint = null; Refresh(); await _music.InitializeAsync();
            ShowMusicOnboarding();
            if (_music.OnboardingCompleted) _ = _music.RefreshRecommendationsAsync();
        };
        DetachedFromVisualTree += (_, _) => { _music.Changed -= Refresh; _music.Tick -= RefreshPlayback; _music.Tick -= RefreshDock; _music.StopObserving(); };
        Refresh();
    }
    private Border BuildSidebar()
    {
        var panel = new StackPanel { Spacing = 8, Margin = new Thickness(8, 24) };
        panel.Children.Add(NavButton("home", "Início", "home", false));
        panel.Children.Add(NavButton("explore", "Buscar", "search", false));
        panel.Children.Add(NavButton("library", "Biblioteca", "library", false));
        panel.Children.Add(NavButton("queue", "Fila", "queue", false));
        panel.Children.Add(new Border { Height = 1, Background = UiTheme.Brush(UiTheme.Outline), Margin = new Thickness(8, 18) });
        var import = SidebarAction("folder", "Importar músicas", OpenLocalAsync);
        import.HorizontalAlignment = HorizontalAlignment.Stretch;
        var tools = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        tools.Children.Add(IconButton("folder", "Abrir áudio local", OpenLocalAsync));
        tools.Children.Add(IconButton("settings", "Conta e configurações", ShowAccountMenuAsync));
        panel.Children.Add(tools);
        panel.Children.Add(import);
        panel.Children.Add(SidebarAction("queue", "Nova playlist", () => { ShowNewPlaylist(); return Task.CompletedTask; }));
        panel.Children.Add(_sidebarPins);
        return new Border { Child = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, Background = UiTheme.Brush(UiTheme.Background) };
    }
    private Button SidebarAction(string icon, string label, Func<Task> action)
    {
        var button = ActionButton("", action);
        var row = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
        row.Children.Add(Icons.Make(icon, Muted, 20));
        var caption = Text(label == "Importar músicas" ? "Importar" : "Playlist +", UiTheme.Label); caption.HorizontalAlignment = HorizontalAlignment.Center; row.Children.Add(caption);
        button.Content = row; button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.Padding = new Thickness(4, 8); button.CornerRadius = new(12); ToolTip.SetTip(button, label);
        Avalonia.Automation.AutomationProperties.SetName(button, label);
        return button;
    }
    private Button NavButton(string key, string label, string icon, bool vertical)
    {
        var button = ActionButton("", () =>
        {
            _queueOpenedFromPlayer = false;
            if (_mobileCollections) CloseTopMenu();
            if (key == "library" && _page == "library" && _mobileCollections) _mobileLibraryGroup = null;
            _page = key;
            BuildListTools();
            Refresh();
            return Task.CompletedTask;
        });
        button.Classes.Add("nav"); button.Tag = (key, label, icon, vertical); _nav.Add(button);
        Avalonia.Automation.AutomationProperties.SetName(button, label);
        button.Padding = UiTheme.Zero; button.HorizontalContentAlignment = HorizontalAlignment.Center;
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
        var next = IconButton("next", "Próxima faixa", _music.NextAsync); next.Classes.Add("subtle");
        open.Content = metadata; row.Children.Add(open); Put(row, _play, 0, 1); Put(row, next, 0, 2);
        grid.Children.Add(row); Put(grid, _progress, 1, 0);
        var card = UiTheme.Card(grid, UiTheme.ControlRadius); card.Margin = UiTheme.MiniMargin; card.ClipToBounds = true;
        card.MaxWidth = UiTheme.PageMaxWidth - UiTheme.PageMargin.Left - UiTheme.PageMargin.Right; return card;
    }
    private async Task SearchAsync()
    {
        _page = "explore";
        BuildListTools();
        var query = _search.Text?.Trim() ?? "";
        if (_searchScope == "Sua biblioteca" || _mobileCollections && query.Length == 0)
        {
            Refresh();
            return;
        }
        if (_mobileCollections) _mobileSearchAttempt = query;
        await _music.SearchAsync(query);
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
        if (!_mobileCollections && _page == "queue" && Bounds.Width >= 1100) { _page = "home"; _queueOpen = true; }
        if (_page != "home" && _listTools.Children.Count == 0) BuildListTools();
        _statusBox.IsVisible = _music.HasError;
        _searchLoading.IsVisible = _music.Searching;
        UiTheme.StateBanner(_statusBox, _music.HasError ? UiTheme.Error : UiTheme.Warning);
        _status.Text = _music.Status; _cancelSearch.IsVisible = _music.Searching;
        foreach (var button in _nav)
        {
            var (key, label, icon, vertical) = ((string, string, string, bool))button.Tag!;
            var active = key == _page || !_mobileCollections && key == "queue" && _queueOpen;
            if (button.Classes.Contains("selected") == active && button.Content is not string) continue;
            button.Classes.Set("selected", active);
            button.Background = Brushes.Transparent;
            button.CornerRadius = new(10); button.Padding = new Thickness(4, 4);
            button.MinHeight = vertical ? 56 : 76;
            var stack = new StackPanel { Spacing = vertical ? 2 : 6, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var image = Icons.Make(icon, active ? Accent : Muted, 22); image.HorizontalAlignment = HorizontalAlignment.Center;
            image.VerticalAlignment = VerticalAlignment.Center;
            var indicator = new Border { Child = image, Width = 52, Height = vertical ? 28 : 32,
                CornerRadius = new(16), Background = active ? UiTheme.Brush("#292929") : Brushes.Transparent };
            stack.Children.Add(indicator);
            var caption = UiTheme.Text(label, UiTheme.Label, active ? UiTheme.TextPrimary : Muted, active ? FontWeight.SemiBold : FontWeight.Normal);
            caption.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(caption); button.Content = stack;
        }
        IReadOnlyList<Track> tracks = IsMobileWorkspace ? [] : _page switch { "library" => _music.Favorites, "queue" => _music.Queue, _ => _searchScope == "Sua biblioteca" ? _music.Favorites.Where(t => (t.Title + " " + t.Artist).Contains(_search.Text ?? "", StringComparison.CurrentCultureIgnoreCase)).ToArray() : _music.Results };
        if (_page == "library" && !_mobileCollections)
        {
            var query = _libraryFilter.Text?.Trim() ?? "";
            tracks = tracks.Where(t => (_libraryKind != "Locais" || t.IsLocal) && (_libraryKind != "Online" || !t.IsLocal)
                && (query.Length == 0 || (t.Title + " " + t.Artist).Contains(query, StringComparison.CurrentCultureIgnoreCase))).ToArray();
            if (_librarySorted) tracks = tracks.OrderBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase).ToArray();
            if (_libraryOrder == "Artista") tracks = tracks.OrderBy(t => t.Artist, StringComparer.CurrentCultureIgnoreCase).ToArray();
            if (_libraryOrder == "Recentes") tracks = tracks.OrderByDescending(t => _music.Listening.GetValueOrDefault(MusicController.SongKey(t))?.LastPlayed ?? DateTime.MinValue).ToArray();
        }
        _brandIcon.IsVisible = _page == "home";
        _pageTitle.Text = _page switch { "home" => Bounds.Width < UiTheme.DesktopWidth ? "Hibiki" : "Hibike", "library" => "Sua biblioteca", "queue" => "Sua fila", _ => "Buscar" };
        if (_main.Child is Grid layout) layout.RowSpacing = 12;
        _listTools.IsVisible = _page != "home";
        _homeScroll.IsVisible = _page == "home"; _trackScroll.IsVisible = _listHeading.IsVisible = _page != "home";
        _search.IsVisible = _page is not ("home" or "library");
        if (_page == "home") RefreshHome();
        RefreshWorkspace();
        _heading.Text = _page switch { "library" => "Músicas salvas", "queue" => "A seguir", _ => "Resultados" };
        _count.Text = tracks.Count + (tracks.Count == 1 ? " faixa" : " faixas");
        var fingerprint = _page + ":" + string.Join("|", tracks.Select(t => t.Id + _music.IsFavorite(t)));
        if (fingerprint != _listFingerprint)
        {
        _listFingerprint = fingerprint; _rows.Clear(); _rowTitles.Clear(); _equalizers.Clear(); _tracks.ItemsSource = tracks;
        }
        _emptyState.IsVisible = !IsMobileWorkspace && _page is not ("home" or "queue" or "collection") && tracks.Count == 0 && !_music.Searching;
        if (_emptyState.IsVisible) _emptyState.Child = EmptyState(_page == "library" ? "library" : _page == "queue" ? "queue" : "search",
            _page == "library" ? "Sua coleção, do seu jeito" : _page == "queue" ? "O próximo som é com você" : "Encontre seu próximo som",
            _page == "library" ? "Salve favoritos ou importe arquivos. Use filtros para encontrar suas músicas." : _page == "queue" ? "Adicione faixas pelo menu de cada música ou toque sua seleção." : "Busque por música, artista ou cole um link de álbum ou playlist.",
            _page == "library" ? "Importar músicas" : "Explorar músicas", _page == "library" ? OpenLocalAsync : () => { _page = "explore"; BuildListTools(); Refresh(); _search.Focus(); return Task.CompletedTask; });
        var displayed = _music.Pending ?? _music.Current;
        _mini.IsVisible = displayed is not null && !_showingPlayer && !(_mobileCollections && _page == "queue");
        _desktopDock.IsVisible = displayed is not null && !_showingPlayer && Bounds.Width >= 1000 && !(_mobileCollections && _page == "queue");
        if (_desktopDock.IsVisible) _mini.IsVisible = false;
        UpdateRows(displayed);
        if (!Equals(displayed, _shownTrack)) { _shownTrack = displayed; _nowArt.Content = new Artwork(displayed, UiTheme.MiniArtworkSize); }
        _nowTitle.Text = TrackTextFormatter.Title(displayed?.Title, displayed?.Artist);
        _nowArtist.Text = TrackTextFormatter.Artist(displayed?.Artist);
        if (_detailPlay is not null && displayed is not null && !Equals(_detailTrack, displayed)) ShowPlayer();
        RefreshPlayback();
        RefreshDiscovery();
        RefreshMobileCollections();
    }
    private sealed record HomeCard(Track Track, Func<Control> Create);
    private static HomeCard Card(Track track, Func<Control> create) => new(track, create);
    private void RefreshHome()
    {
        var fingerprint = string.Join("|", _music.History.Select(t => t.Source)) + ":" + string.Join("|", _music.Favorites.Select(t => t.Source))
            + ":" + string.Join("|", _music.Pins.Select(t => t.Source)) + ":" + string.Join("|", _music.Playlists.Select(p => p.Name + p.Tracks.Count))
            + ":" + (_account is not null) + ":" + _accountLoading + ":" + string.Join("|", _accountLikes.Select(t => t.Id))
            + ":" + string.Join("|", _accountPlaylists.Select(p => p.Id)) + ":" + _music.Current?.Source + ":" + (Bounds.Width < UiTheme.DesktopWidth)
            + ":" + string.Join("|", _music.Queue.Select(t => t.Source))
            + ":" + string.Join("|", _music.FavoriteArtists.Select(a => a.Id)) + ":" + string.Join("|", _music.HomeRecommendations.Select(t => t.Source))
            + ":" + _music.HomeLoading + ":" + _music.HomeMessage + ":" + _music.RegionalChart?.RetrievedUtc + ":" + _music.GlobalChart?.RetrievedUtc + ":" + _music.TrendingChart?.RetrievedUtc;
        if (fingerprint == _homeFingerprint) return;
        _homeFingerprint = fingerprint;
        var previous = _home.Children.ToArray();
        var next = new List<Control>();
        var shownRecommendations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Shelf("Continuar ouvindo", "Fixadas e histórico · neste aparelho", _music.Pins.Concat(_music.History).DistinctBy(t => t.Source).Take(20).Select(t => Card(t, () => CoverCard(t, TrackTextFormatter.Artist(t.Artist), async () => { if (_music.Current?.Source == t.Source) await _music.ToggleAsync(); else await _music.PlayAsync(t); }))));
        if (_music.History.Count > 0) next.Add(ActionButton("Ver histórico", () => { OpenLocalCollection("Histórico", _music.History); return Task.CompletedTask; }));
        Shelf("Feito para você", "Suas preferências, favoritos e histórico · neste aparelho", _music.HomeRecommendations.Take(12).Select(t => Card(t, () => TrackCard(t))));
        Shelf("Seus artistas favoritos", "Suas escolhas · toque para explorar", _music.FavoriteArtists.Select(a =>
        {
            var artist = new Track(a.Id, a.Name, a.Name, "https://music.youtube.com/channel/" + a.Id, Thumbnail: a.Image);
            return Card(artist, () =>
            {
                var card = CoverCard(artist, "Artista", () => ShowArtistAsync(a), songActions: false);
                foreach (var artwork in card.GetVisualDescendants().OfType<Artwork>()) artwork.CornerRadius = new(120);
                return card;
            });
        }));
        Shelf("Porque você gosta de " + _music.HomeSeed, "Rádio YouTube Music · sugestões relacionadas", _music.HomeRelated.Take(12).Select(t => Card(t, () => TrackCard(t))));
        var familiar = _music.History.Select(t => TrackTextFormatter.Artist(t.Artist)).Concat(_music.FavoriteArtists.Select(a => a.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Shelf("Descubra algo novo", "Artistas ainda não explorados", _music.HomeRecommendations.Skip(12).Where(t => !familiar.Contains(TrackTextFormatter.Artist(t.Artist))).Take(12).Select(t => Card(t, () => TrackCard(t))));
        var albumTracks = _music.HomeRecommendations.Concat(_music.Favorites).Where(t => !string.IsNullOrWhiteSpace(t.Album)).GroupBy(t => (t.Artist, t.Album));
        Shelf("Álbuns para você", "Álbuns identificados no catálogo · faixas disponíveis", albumTracks.Take(12).Select(g => Card(g.First() with { Title = g.Key.Album! }, () => CoverCard(g.First() with { Title = g.Key.Album! }, g.Key.Artist, () => { OpenLocalCollection(g.Key.Album!, g); return Task.CompletedTask; }, songActions: false))));
        void ChartShelf(MusicChart? chart, string title)
        {
            if (chart is null || DateTime.UtcNow - chart.RetrievedUtc > TimeSpan.FromDays(7)) return;
            Shelf(title, chart.Title + " · YouTube Music · consultado " + chart.RetrievedUtc.ToLocalTime().ToString("g"), chart.Tracks.Take(20).Select(t => Card(t, () => TrackCard(t))));
        }
        ChartShelf(_music.RegionalChart, _music.ChartRegion == "BR" ? "Em alta no Brasil" : "Em alta · " + _music.ChartRegion);
        ChartShelf(_music.GlobalChart, "Em alta no mundo");
        if (_music.TrendingChart is { } trending && DateTime.UtcNow - trending.RetrievedUtc <= TimeSpan.FromHours(12))
        {
            Shelf("Bombando agora", trending.Title + " · YouTube Music · atualizado algumas vezes ao dia · consultado " + trending.RetrievedUtc.ToLocalTime().ToString("g"), trending.Tracks.Take(20).Select(t => Card(t, () => TrackCard(t))));
            Shelf("Tendências para você", trending.Title + " · seus artistas e descobertas", trending.Tracks.OrderByDescending(t => familiar.Contains(TrackTextFormatter.Artist(t.Artist))).Take(12).Select(t => Card(t, () => TrackCard(t))));
        }
        if (_music.HomeLoading || !string.IsNullOrEmpty(_music.HomeMessage)) next.Add(Text(_music.HomeLoading ? "Preparando descobertas… Sua biblioteca já está disponível." : _music.HomeMessage, UiTheme.Caption, Muted));
        next.Add(ActionButton("Personalizar meu som", () => { ShowMusicOnboarding(edit: true); return Task.CompletedTask; }));
        next.Add(ActionButton("Atualizar recomendações", () => _music.RefreshRecommendationsAsync(true)));
        var recommended = _music.Queue.Where(_music.IsRecommended).Take(12).ToArray();
        if (recommended.Length > 0) Shelf("Porque você ouviu…", _music.PlaybackContext, recommended.Select(t => Card(t, () => TrackCard(t))));
        if (_music.History.Count >= 5)
            Shelf("Seus mixes", "Rádios por artistas do seu histórico", _music.History.GroupBy(t => t.Artist).Where(g => g.Count() >= 2).Take(4).Select(g => Card(g.First(), () => CoverCard(g.First(), "Mix de " + g.Key, () => _music.StartRadioAsync(g.First())))));
        if (_music.History.Count == 0) next.Add(EmptyState("play", "Sua trilha começa aqui", "Suas últimas músicas aparecem aqui automaticamente. Comece com uma busca ou traga seus arquivos.", "Buscar música", () => { _page = "explore"; BuildListTools(); Refresh(); _search.Focus(); return Task.CompletedTask; }));
        if (_account is not null)
        {
            Shelf("Suas curtidas no YouTube", "Biblioteca da conta · não são recomendações", _accountLikes.Take(30).Select(t => Card(t, () => TrackCard(t))));
            Shelf("Suas playlists", "Playlists da conta YouTube", _accountPlaylists.Take(50).Select(p =>
            {
                var art = new Track(p.Id, p.Title, "Playlist", "https://www.youtube.com/playlist?list=" + Uri.EscapeDataString(p.Id), Thumbnail: p.Thumbnail);
                return Card(art, () => CoverCard(art, "Playlist", () => ShowAccountTracksAsync(_account!, p.Id, p.Title), playlist: true));
            }));
            var update = ActionButton(_accountLoading ? "Carregando…" : "Atualizar biblioteca", LoadAccountHomeAsync); update.HorizontalAlignment = HorizontalAlignment.Left; next.Add(update);
        }
        if (Bounds.Width >= UiTheme.DesktopWidth && _music.Queue.Count > 0)
            Shelf("A seguir", "Próximas músicas da sua fila", _music.Upcoming.Take(20).Select(t => Card(t, () => TrackCard(t))));
        Shelf("Suas músicas favoritas", "Favoritos e arquivos salvos no Hibike", _music.Favorites.OrderByDescending(t => _music.Listening.GetValueOrDefault(MusicController.SongKey(t))?.LastPlayed).Take(30).Select(t => Card(t, () => TrackCard(t))));
        if (_music.Favorites.Count == 0) next.Add(ActionButton("Importar músicas para sua coleção", OpenLocalAsync));
        foreach (var removed in previous.Where(c => !next.Contains(c))) _home.Children.Remove(removed);
        for (var i = 0; i < next.Count; i++)
        {
            var item = next[i];
            var index = _home.Children.IndexOf(item);
            if (index == i) continue;
            if (index >= 0) _home.Children.Move(index, i);
            else { UiMotion.Reflow(item); _home.Children.Insert(i, item); Animate(item); }
        }

        void Shelf(string title, string subtitle, IEnumerable<HomeCard> cards)
        {
            var personalized = title == "Feito para você" || title == "Descubra algo novo" || title.StartsWith("Porque você gosta de ") || title == "Tendências para você";
            var cardList = cards.Where(c => !personalized || shownRecommendations.Add(c.Track.Source)).ToArray();
            if (cardList.Length == 0) return;
            var key = title + ":" + subtitle + ":" + (Bounds.Width < UiTheme.DesktopWidth) + ":" + string.Join("|", cardList.Select(c => c.Track.ToString()));
            var old = previous.OfType<StackPanel>().FirstOrDefault(c => c.Tag is ValueTuple<string, string> tag && tag.Item1 == title);
            if (old?.Tag is ValueTuple<string, string> oldTag && oldTag.Item2 == key) { next.Add(old); return; }
            var section = new StackPanel { Spacing = 6, Margin = new Thickness(0, Bounds.Width < UiTheme.DesktopWidth ? 6 : 14, 0, 0) };
            section.Tag = (title, key);
            var header = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 };
            var heading = Text(title, UiTheme.DialogTitle); heading.FontWeight = FontWeight.SemiBold; heading.TextTrimming = TextTrimming.CharacterEllipsis; header.Children.Add(heading);
            var controls = new StackPanel { Orientation = Orientation.Horizontal };
            var scroll = UiTheme.FadingScroll(horizontal: true);
            controls.Children.Add(IconButton("back", "Voltar em " + title, () => { scroll.Offset = new Vector(Math.Max(0, scroll.Offset.X - Math.Max(180, scroll.Viewport.Width * .8)), 0); return Task.CompletedTask; }));
            controls.Children.Add(IconButton("forward", "Avançar em " + title, () => { scroll.Offset = new Vector(Math.Min(Math.Max(0, scroll.Extent.Width - scroll.Viewport.Width), scroll.Offset.X + Math.Max(180, scroll.Viewport.Width * .8)), 0); return Task.CompletedTask; }));
            Put(header, controls, 0, 1); section.Children.Add(header);
            var note = Text(subtitle, UiTheme.Caption, Muted); note.TextWrapping = TextWrapping.Wrap; section.Children.Add(note);
            var row = new ItemsControl
            {
                ItemsSource = cardList,
                ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel
                { Orientation = Orientation.Horizontal, CacheLength = .5 }),
                ItemTemplate = new FuncDataTemplate<HomeCard>((card, _) =>
                {
                    if (card is null) return new Border();
                    var control = card.Create();
                    control.Margin = new Thickness(0, 0, 18, 0);
                    return control;
                })
            };
            controls.IsVisible = cardList.Length > 1;
            scroll.Content = row; section.Children.Add(scroll);
            if (old?.Children.LastOrDefault() is ScrollViewer oldScroll)
            {
                var offset = oldScroll.Offset;
                scroll.AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() => scroll.Offset = offset, DispatcherPriority.Loaded);
            }
            next.Add(section);
        }
    }
    private Control TrackCard(Track track)
    {
        return CoverCard(track, TrackTextFormatter.Artist(track.Artist), async () => { await _music.PlayAsync(track); });
    }
    private Control CoverCard(Track track, string subtitle, Func<Task> action, bool playlist = false, bool songActions = true, double? cardSize = null, Stretch artworkStretch = Stretch.UniformToFill)
    {
        var size = cardSize ?? (Bounds.Width < UiTheme.DesktopWidth ? (playlist ? 168 : 160) : (playlist ? 204 : 196));
        var panel = new StackPanel { Width = size, Spacing = 4 };
        var cover = new Grid { Width = size, Height = size };
        var artwork = new Artwork(track, size, stretch: artworkStretch, highQuality: true)
        {
            CornerRadius = new(16), Background = Brushes.Transparent, BorderThickness = new Thickness(0)
        };
        cover.Children.Add(artwork);
        var playOverlay = new Button { Content = Icons.Make("play"), IsHitTestVisible = false, Focusable = false,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 60, 8), IsVisible = false };
        playOverlay.Classes.Add("cardAction");
        cover.Children.Add(playOverlay);
        panel.Children.Add(cover);
        var title = Text(TrackTextFormatter.Title(track.Title, track.Artist), UiTheme.Body);
        title.TextWrapping = TextWrapping.Wrap; title.MaxLines = 2; title.VerticalAlignment = VerticalAlignment.Top; title.TextTrimming = TextTrimming.CharacterEllipsis; panel.Children.Add(title);
        var artist = Text(subtitle, UiTheme.Caption, Muted); artist.TextTrimming = TextTrimming.CharacterEllipsis; panel.Children.Add(artist);
        var button = ActionButton("", action); button.Content = panel; button.Classes.Add("cover");
        button.Padding = new Thickness(0); button.Background = Brushes.Transparent;
        button.VerticalContentAlignment = VerticalAlignment.Top;
        Avalonia.Automation.AutomationProperties.SetName(button, track.Title + " — " + subtitle);
        ToolTip.SetTip(button, track.Title + "\n" + subtitle);
        var host = new Grid { VerticalAlignment = VerticalAlignment.Top, Tag = track }; host.Children.Add(button);
        var more = IconButton("more", "Opções de " + track.Title, () => Task.CompletedTask);
        more.HorizontalAlignment = HorizontalAlignment.Right; more.VerticalAlignment = VerticalAlignment.Top;
        more.Classes.Remove("round"); more.Classes.Add("cardAction");
        more.Margin = new Thickness(0, size - 56, 8, 0); more.IsVisible = false;
        more.Click += (_, e) => { e.Handled = true; ShowSongMenu(track, more); }; host.Children.Add(more);
        void SetActions(bool visible) { playOverlay.IsVisible = !OperatingSystem.IsAndroid() && visible; more.IsVisible = songActions && playOverlay.IsVisible; }
        host.PointerEntered += (_, _) => SetActions(true);
        host.PointerExited += (_, _) => SetActions(host.IsKeyboardFocusWithin);
        host.GotFocus += (_, _) => SetActions(true);
        host.LostFocus += (_, _) => SetActions(host.IsPointerOver);
        if (songActions) AttachSongHold(button, track);
        return host;
    }
    private async Task LoadAccountHomeAsync()
    {
        if (_account is not { } account || _accountLoading) return;
        _accountLoading = true; Refresh();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var playlists = await account.PlaylistsAsync(timeout.Token);
            var likes = await account.TracksAsync(null, timeout.Token);
            if (!ReferenceEquals(_account, account)) return;
            _accountPlaylists = playlists; _accountLikes = likes;
        }
        finally { _accountLoading = false; Refresh(); }
    }
    private void DisconnectAccount()
    {
        _account = null; _accountPlaylists = []; _accountLikes = []; Refresh();
    }
    private Control EmptyState(string icon, string title, string message, string? action = null, Func<Task>? execute = null)
    {
        var panel = new StackPanel { Spacing = 14, MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(12, 24) };
        panel.Children.Add(Icons.Make(icon, Muted, 40));
        var heading = Text(title, UiTheme.DialogTitle); heading.TextWrapping = TextWrapping.Wrap; heading.TextAlignment = TextAlignment.Center; panel.Children.Add(heading);
        var body = Text(message, UiTheme.Body, Muted); body.TextWrapping = TextWrapping.Wrap; body.TextAlignment = TextAlignment.Center; panel.Children.Add(body);
        if (action is not null && execute is not null)
        {
            var button = ActionButton(action, execute); button.Classes.Add("primary"); button.HorizontalAlignment = HorizontalAlignment.Center; panel.Children.Add(button);
        }
        return panel;
    }
    private void BuildListTools()
    {
        _listTools.Children.Clear();
        if (IsMobileWorkspace) return;
        if (_page == "library") _listTools.Children.Add(_libraryFilter);
        var row = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 8 };
        if (_page == "library")
            foreach (var section in new[] { "Músicas", "Playlists", "Álbuns", "Artistas" })
            {
                var tab = ActionButton(section, () => { _librarySection = section; BuildListTools(); Refresh(); return Task.CompletedTask; });
                if (_librarySection == section) tab.Classes.Add("primary"); row.Children.Add(tab);
            }
        if (_page == "library")
        {
            foreach (var label in new[] { "Todas", "Locais", "Online" })
            {
                var chip = ActionButton(label, () => { _libraryKind = label; BuildListTools(); Refresh(); return Task.CompletedTask; }); chip.Classes.Add("pill");
                if (_libraryKind == label) chip.Classes.Add("primary"); row.Children.Add(chip);
            }
            row.Children.Add(ActionButton(_libraryOrder, () => { _libraryOrder = _libraryOrder == "Recentes" ? "Título A–Z" : _libraryOrder == "Título A–Z" ? "Artista" : "Recentes"; _librarySorted = _libraryOrder == "Título A–Z"; BuildListTools(); Refresh(); return Task.CompletedTask; }));
            if (_librarySection == "Playlists") row.Children.Add(ActionButton("Nova playlist", () => { ShowNewPlaylist(); return Task.CompletedTask; }));
            if (_librarySection == "Músicas") row.Children.Add(ActionButton("Tocar tudo", () => PlayCollectionAsync(_tracks.ItemsSource?.Cast<Track>() ?? [])));
        }
        else if (_page == "queue")
        {
            row.Children.Add(ActionButton("Embaralhar", () => { _music.ReorderQueue(true); return Task.CompletedTask; }));
            row.Children.Add(ActionButton(_music.RepeatQueue ? "Repetir: ligado" : "Repetir fila", () => { _music.RepeatQueue = !_music.RepeatQueue; BuildListTools(); return Task.CompletedTask; }));
        }
        else
        {
            foreach (var scope in new[] { "Online", "Sua biblioteca" })
                row.Children.Add(ActionButton(scope, async () => { _searchScope = scope; await SearchAsync(); }));
            foreach (var (label, query) in new[] { ("Explorar gêneros", "jazz soul instrumental music"), ("Foco", "instrumental focus music"), ("Energia", "energetic music"), ("Trilhas sonoras", "original soundtrack music") })
                row.Children.Add(ActionButton(label, async () => { _searchScope = "Online"; _search.Text = query; await SearchAsync(); }));
            row.Children.Add(ActionButton("Adicionar resultados à fila", () => { _music.EnqueueResults(); return Task.CompletedTask; }));
            row.Children.Add(ActionButton("Tocar resultados", () => PlayCollectionAsync(_music.Results)));
        }
        _listTools.Children.Add(row);
    }
    private async Task PlayCollectionAsync(IEnumerable<Track> collection)
    {
        var tracks = collection.ToArray(); if (tracks.Length == 0) return;
        _music.ReplaceQueue(tracks);
        await _music.PlayAsync(tracks[0], 0);
    }
    private static void AddSearchIcon(TextBox field) =>
        field.InnerLeftContent = new Border { Child = Icons.Make("search", Muted), Padding = UiTheme.SearchIconPadding };

    private Control TrackRow(Track track, bool showMenu = false, Func<Task>? playTrack = null, Action<Control>? openMenu = null,
        string? subtitle = null)
    {
        var grid = new Grid { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = UiTheme.CompactGap, Height = UiTheme.ThumbnailSize };
        var play = ActionButton("", playTrack ?? (() => _music.PlayAsync(track, _music.Queue.Contains(track) ? _music.Queue.IndexOf(track) : null))); play.Classes.Add("subtle"); play.Padding = UiTheme.Zero; play.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Avalonia.Automation.AutomationProperties.SetName(play, track.Title + " — " + track.Artist);
        var metadata = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = UiTheme.CompactGap };
        var thumbnail = new Grid(); thumbnail.Children.Add(new Artwork(track, UiTheme.ThumbnailSize));
        var equalizer = new Equalizer(); thumbnail.Children.Add(equalizer); metadata.Children.Add(thumbnail);
        var labels = new StackPanel { Spacing = UiTheme.ProgressRadius, VerticalAlignment = VerticalAlignment.Center };
        var title = Text(TrackTextFormatter.Title(track.Title, track.Artist), UiTheme.Body); title.TextTrimming = TextTrimming.CharacterEllipsis; labels.Children.Add(title);
        var artist = Text(subtitle ?? TrackTextFormatter.Artist(track.Artist), UiTheme.Caption, Muted); artist.TextTrimming = TextTrimming.CharacterEllipsis; labels.Children.Add(artist);
        Put(metadata, labels, 0, 1); play.Content = metadata;
        grid.Children.Add(play);
        if (track.DurationSeconds > 0 || !showMenu) Put(grid, Text(track.DurationLabel, UiTheme.Caption, Muted), 0, 1);
        var more = IconButton("more", "Opções de " + track.Title, () => Task.CompletedTask);
        openMenu ??= anchor => ShowSongMenu(track, anchor);
        more.Click += (_, _) => openMenu(more);
        Put(grid, more, 0, 2);
        more.Classes.Add("subtle");
        AttachSongHold(play, track, openMenu);
        more.IsVisible = showMenu || !OperatingSystem.IsAndroid();
        var border = new Border { Child = grid, Padding = UiTheme.RowPadding, Height = OperatingSystem.IsAndroid() || showMenu ? 64 : UiTheme.TrackRowHeight };
        if (showMenu)
        {
            void Update()
            {
                var selected = (_music.Pending ?? _music.Current)?.Source == track.Source;
                UpdateTrackRowAppearance(border, title, equalizer, selected);
            }
            border.AttachedToVisualTree += (_, _) => { _music.Changed += Update; _music.Tick += Update; Update(); };
            border.DetachedFromVisualTree += (_, _) => { _music.Changed -= Update; _music.Tick -= Update; };
            Update();
            return border;
        }
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
            UpdateTrackRowAppearance(row, _rowTitles[id], _equalizers[id], id == displayed?.Id);
    }

    private void UpdateTrackRowAppearance(Border row, TextBlock title, Equalizer equalizer, bool selected)
    {
        var playing = selected && _music.Player.IsPlaying;
        equalizer.SetState(selected, playing);
        if (row.Tag is bool wasSelected && wasSelected == selected) return;
        row.Tag = selected;
        row.Background = selected ? UiTheme.Brush(UiTheme.PrimaryContainer) : Brushes.Transparent;
        row.BorderBrush = UiTheme.Brush(selected ? UiTheme.Primary : UiTheme.Outline);
        row.BorderThickness = selected ? UiTheme.One : UiTheme.BottomBorder;
        row.CornerRadius = new(selected ? UiTheme.ControlRadius : 0);
        title.FontWeight = selected ? FontWeight.SemiBold : FontWeight.Normal;
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
        Put(header, IconButton("queue", "Abrir fila", () => { OpenQueueFromPlayer(); return Task.CompletedTask; }), 0, 2); panel.Children.Add(header);
        var artworkTrack = track == _music.Current && _music.CurrentSource?.Artwork is { } resolvedArtwork ? track with { Thumbnail = resolvedArtwork } : track;
        var art = new Artwork(artworkTrack, UiTheme.ThumbnailSize, fullResolution: true) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        art.CornerRadius = new(UiTheme.CardRadius); Put(panel, art, 1, 0);
        var metadata = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = UiTheme.CompactGap };
        var labels = new StackPanel { Spacing = UiTheme.CompactGap };
        var title = Text(TrackTextFormatter.Title(track.Title, track.Artist), UiTheme.DialogTitle); title.FontWeight = FontWeight.SemiBold; title.TextWrapping = TextWrapping.Wrap; title.MaxLines = 2; labels.Children.Add(title);
        var artist = Text(TrackTextFormatter.Artist(track.Artist), UiTheme.Body, Muted); artist.TextTrimming = TextTrimming.CharacterEllipsis; labels.Children.Add(artist); metadata.Children.Add(labels);
        var favorite = _detailFavorite = IconButton("heart", "Alternar favorito", TogglePlaybackFavoriteAsync);
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
        var shuffle = _detailShuffle = IconButton("shuffle", "Aleatório", TogglePlaybackShuffleAsync); shuffle.HorizontalAlignment = HorizontalAlignment.Center; controls.Children.Add(shuffle);
        var previous = IconButton("previous", "Anterior", _music.PreviousAsync); previous.Content = Icons.Make("previous", UiTheme.TextPrimary, UiTheme.TransportIconSize); previous.HorizontalAlignment = HorizontalAlignment.Center; Put(controls, previous, 0, 1);
        _detailPlay = IconButton("play", "Reproduzir ou pausar", _music.ToggleAsync); _detailPlay.Classes.Add("primary"); _detailPlay.Classes.Add("largePlay"); _detailPlay.Width = _detailPlay.Height = UiTheme.PlaySize; _detailPlay.CornerRadius = new(UiTheme.PlaySize / 2); Put(controls, _detailPlay, 0, 2);
        var next = IconButton("next", "Próxima", _music.NextAsync); next.Content = Icons.Make("next", UiTheme.TextPrimary, UiTheme.TransportIconSize); next.HorizontalAlignment = HorizontalAlignment.Center; Put(controls, next, 0, 3);
        var repeat = _detailRepeat = IconButton("repeat", "Repetir", CyclePlaybackRepeatAsync); repeat.HorizontalAlignment = HorizontalAlignment.Center; Put(controls, repeat, 0, 4); Put(panel, controls, 5, 0);
        foreach (var button in new[] { shuffle, previous, next, repeat }) button.Classes.Add("subtle");
        var upcoming = new StackPanel { Spacing = UiTheme.CompactGap };
        upcoming.Children.Add(UiTheme.Text("Próxima", UiTheme.Label, Muted, FontWeight.Medium));
        var nextTrack = _music.Upcoming.FirstOrDefault();
        var nextTitle = _detailNext = Text(nextTrack is null ? "Nenhuma faixa na sequência" : TrackTextFormatter.Title(nextTrack.Title, nextTrack.Artist), UiTheme.ButtonText);
        nextTitle.TextTrimming = TextTrimming.CharacterEllipsis; upcoming.Children.Add(nextTitle);
        var upcomingBox = UiTheme.Inner(upcoming); Put(panel, upcomingBox, 6, 0);
        panel.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);
        panel.RowDefinitions[4].Height = new GridLength(UiTheme.PlayerBottomWeight, GridUnitType.Star);
        var desktopLayout = new Grid { RowDefinitions = new("Auto,*"), ColumnDefinitions = new("*,*"),
            ColumnSpacing = UiTheme.DesktopPlayerGap, RowSpacing = UiTheme.PageGap, Margin = UiTheme.DesktopPlayerMargin,
            HorizontalAlignment = HorizontalAlignment.Stretch };
        var queuePanel = new Grid { RowDefinitions = new("Auto,Auto,*"), RowSpacing = UiTheme.SectionGap };
        desktopLayout.HorizontalAlignment = HorizontalAlignment.Stretch;
        var queueHeader = new Grid { ColumnDefinitions = new("*,Auto") };
        var queueHeading = _detailQueueHeading = UiTheme.Text(_music.PlaybackContext, UiTheme.SectionTitle, weight: FontWeight.SemiBold); queueHeading.TextTrimming = TextTrimming.CharacterEllipsis; queueHeader.Children.Add(queueHeading);
        Put(queueHeader, ActionButton("Limpar fila", () => { _music.ClearQueue(); return Task.CompletedTask; }), 0, 1);
        queuePanel.Children.Add(queueHeader);
        var playerRadio = new WrapPanel { ItemSpacing = 8 };
        playerRadio.Children.Add(_detailRadioToggle = ActionButton("Reprodução automática: " + (_music.Autoplay ? "ligada" : "desligada"), () => { _music.SetAutoplay(!_music.Autoplay); return Task.CompletedTask; }));
        playerRadio.Children.Add(ActionButton("Relacionadas", () => { ClosePlayer(); _relatedTab = true; _page = "queue"; _queueOpen = true; Refresh(); return Task.CompletedTask; }));
        playerRadio.Children.Add(_detailRadioRetry = CreateRadioRetryButton());
        Put(queuePanel, playerRadio, 1, 0);
        _detailQueueFingerprint = null;
        var queueItems = _detailQueue = new ItemsControl { ItemsSource = _music.Upcoming.ToArray(),
            ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()),
            ItemTemplate = new FuncDataTemplate<Track>((item, _) => item is null ? new Border() : TrackRow(item)) };
        Put(queuePanel, UiTheme.FadingScroll(queueItems), 2, 0);
        var transport = new Grid { RowDefinitions = new("Auto,Auto"), ColumnDefinitions = new("*,Auto,*"), ColumnSpacing = UiTheme.PageGap,
            Background = UiTheme.Brush(UiTheme.Surface), Margin = UiTheme.Zero };
        var transportFrame = new Border { Child = transport, Background = UiTheme.Brush(UiTheme.Surface),
             BorderBrush = UiTheme.Brush(UiTheme.Outline), BorderThickness = UiTheme.TopBorder, Padding = UiTheme.DesktopTransportPadding };
        var volumeSlider = _detailVolume = new Slider { Minimum = 0, Maximum = 1, Value = _music.Player.Volume,
            Width = 100, MinHeight = UiTheme.TouchSize, SmallChange = .05, LargeChange = .1,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = UiTheme.Brush(UiTheme.Primary), Background = UiTheme.Brush(UiTheme.Outline) };
        Avalonia.Automation.AutomationProperties.SetName(volumeSlider, "Volume");
        volumeSlider.ValueChanged += (_, _) => _music.Player.Volume = volumeSlider.Value;
        var volumeButton = IconButton("volume", "Ajustar volume", () => { volumeSlider.Focus(); return Task.CompletedTask; });
        var volumeGroup = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        volumeGroup.Children.Add(volumeSlider); volumeGroup.Children.Add(volumeButton);
        var audioTools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        audioTools.Children.Add(volumeGroup);
        audioTools.Children.Add(IconButton("more", "Informações do áudio", () => { ShowAudioInfo(); return Task.CompletedTask; }));
        var sourceButton = IconButton("library", "Fonte de reprodução", ShowPlaybackSourcesAsync);
        sourceButton.IsVisible = false;
        audioTools.Children.Add(sourceButton);
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
                    _expandedQuality = Text("", UiTheme.Caption, Muted);
                    _expandedQuality.TextWrapping = TextWrapping.Wrap;
                    _expandedQuality.TextAlignment = TextAlignment.Center;
                    _expandedQuality.VerticalAlignment = VerticalAlignment.Center;
                    var qualityBadge = UiTheme.AudioBadge(_expandedQuality);
                    qualityBadge.HorizontalAlignment = HorizontalAlignment.Right;
                    Put(transport, qualityBadge, 0, 2);
                    RefreshDock();
                    Put(transport, seek, 1, 0); Grid.SetColumnSpan(seek, 3);
                    Put(host, desktopLayout, 0, 0); Put(host, transportFrame, 1, 0);
                }
                else
                {
                    _expandedQuality = Text("", UiTheme.Caption, Muted);
                    _expandedQuality.TextWrapping = TextWrapping.Wrap;
                    _expandedQuality.TextAlignment = TextAlignment.Center;
                    var mobileQuality = UiTheme.AudioBadge(_expandedQuality);
                    mobileQuality.HorizontalAlignment = HorizontalAlignment.Center;
                    mobileQuality.VerticalAlignment = VerticalAlignment.Top;
                    mobileQuality.Margin = new Thickness(0, 8, 0, 0);
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
                    Put(panel, mobileQuality, 4, 0);
                    RefreshDock();
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
        }
        // Detach initial mobile composition before responsive host takes ownership.
        panel.Children.Clear();
        host.SizeChanged += (_, _) => ArrangePlayer();
        transport.LayoutUpdated += (_, _) => ArrangeTransport();
        _playerPage = new Border { Background = UiTheme.Brush(UiTheme.Background), Child = host };
        Grid.SetColumn(_playerPage, 1); Grid.SetColumnSpan(_playerPage, 2); Grid.SetRowSpan(_playerPage, 3); _root.Children.Add(_playerPage);
        _main.IsVisible = false; _mini.IsVisible = false; _desktopDock.IsVisible = false; _queuePane.IsVisible = false; _bottom.IsVisible = false;
        Animate(_playerPage); RefreshPlayback();
    }
    private void ClosePlayer()
    {
        if (_playerPage is not null) _root.Children.Remove(_playerPage);
        _playerPage = null; _showingPlayer = false; _listFingerprint = null; _main.IsVisible = true; _bottom.IsVisible = Bounds.Width < UiTheme.DesktopWidth;
        _detailShuffle = _detailRepeat = _detailFavorite = null; _detailVolume = null;
        _detailQueue = null; _detailNext = _detailQueueHeading = null; _detailRadioToggle = _detailRadioRetry = null;
        _detailTrack = null; _detailSeek = null; _detailPlay = null; _detailTime = null; _detailDuration = null; _seeking = false; Refresh(); Animate(_main);
    }
    private bool HandleBack()
    {
        if (_googleWelcome is not null) { if (!_googleWelcomeBusy) DismissGoogleWelcome(); return true; }
        if (_topMenuOpen) { CloseTopMenu(); return true; }
        if (_songPopup is { IsOpen: true }) { _songPopup.IsOpen = false; return true; }
        if (_overlay is not null) { CloseSheet(); return true; }
        if (_showingPlayer) { ClosePlayer(); return true; }
        if (_mobileCollections && _page == "library" && _mobileLibraryGroup is not null) { CloseMobileLibraryGroup(); return true; }
        if (_mobileCollections && _page == "queue" && _queueOpenedFromPlayer) { ReturnFromMobileQueue(); return true; }
        if (_page == "collection") { _page = "home"; Refresh(); return true; }
        if (_queueOpen || _page == "queue") { _queueOpen = false; _page = "home"; Refresh(); return true; }
        return false;
    }
    private static void Animate(Control control)
    {
        control.Transitions = null; control.Opacity = 0;
        control.Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(180), Easing = new Avalonia.Animation.Easings.CubicEaseOut() } };
        Dispatcher.UIThread.Post(() => control.Opacity = 1, DispatcherPriority.Render);
    }
    private ItemsControl? _detailQueue;
    private TextBlock? _detailNext, _detailQueueHeading;
    private Button? _detailRadioToggle, _detailRadioRetry;
    private string? _detailQueueFingerprint;
    private void RefreshPlayerQueue()
    {
        if (_detailQueue is null) return;
        var upcoming = _music.Upcoming.ToArray();
        var fingerprint = string.Join("|", upcoming.Select(t => t.Source + ":" + _music.IsRecommended(t)));
        if (_detailQueueFingerprint != fingerprint)
        { _detailQueueFingerprint = fingerprint; _detailQueue.ItemsSource = upcoming; }
        var next = upcoming.FirstOrDefault();
        _detailNext!.Text = next is null ? "Nenhuma faixa na sequência" : TrackTextFormatter.Title(next.Title, next.Artist);
        _detailQueueHeading!.Text = _music.PlaybackContext;
        _detailRadioToggle!.Content = "Reprodução automática: " + (_music.Autoplay ? "ligada" : "desligada");
        _detailRadioRetry!.Content = _music.RadioLoading ? "Buscando sugestões…" : "Buscar sugestões novamente";
        _detailRadioRetry.IsEnabled = _music.Current is not null && !_music.RadioLoading && !_music.LoadingAudio;
    }
    private void RefreshPlayback()
    {
        RefreshPlaybackActions();
        if (_mobileCollections && _page == "queue" && _mobileQueuePlay is not null) RefreshPlayButton(_mobileQueuePlay);
        RefreshPlayerQueue();
        UpdateRows(_music.Pending ?? _music.Current);
        RefreshPlayButton(_play);
        if (_detailPlay is not null) RefreshPlayButton(_detailPlay);
        var duration = PlaybackDuration;
        _progress.Maximum = Math.Max(1, duration); _progress.Value = Math.Min(_music.Position, _progress.Maximum);
        if (_detailSeek is not null)
        {
            _detailSeek.Maximum = Math.Max(1, duration); _detailSeek.IsEnabled = duration > 0 && !_music.LoadingAudio;
            if (!_seeking) _detailSeek.Value = Math.Min(_music.Position, _detailSeek.Maximum);
        }
        if (_detailTime is not null) _detailTime.Text = Time(_music.Position);
        if (_detailDuration is not null) _detailDuration.Text = Time(duration);
    }
    private void RefreshPlayButton(Button button)
    {
        var state = _music.LoadingAudio ? "loading" : _music.Player.IsPlaying ? "pause" : "play";
        if (button.Content is Control existing && Equals(existing.Tag, state)) return;
        Control content;
        if (state == "loading")
            content = LoadingRing(22, UiTheme.OnPrimary);
        else content = Icons.Make(state, UiTheme.OnPrimary, UiTheme.TransportIconSize);
        content.Tag = state; button.Content = content;
        Avalonia.Automation.AutomationProperties.SetName(button, state == "loading" ? "Carregando áudio; cancelar" : "Reproduzir ou pausar");
    }
    private static Control LoadingRing(double size, string color)
    {
            var ring = new Avalonia.Controls.Shapes.Ellipse
            {
                Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center, Stroke = UiTheme.Brush(color), StrokeThickness = 2,
                StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 22, 12 },
                RenderTransform = new RotateTransform(), RenderTransformOrigin = RelativePoint.Center
            };
            var animation = new Animation { Duration = TimeSpan.FromMilliseconds(800), IterationCount = IterationCount.Infinite };
            animation.Children.Add(new KeyFrame { Cue = new Cue(0), Setters = { new Avalonia.Styling.Setter(RotateTransform.AngleProperty, 0d) } });
            animation.Children.Add(new KeyFrame { Cue = new Cue(1), Setters = { new Avalonia.Styling.Setter(RotateTransform.AngleProperty, 360d) } });
            ring.Styles.Add(new Avalonia.Styling.Style(s => s.OfType<Avalonia.Controls.Shapes.Ellipse>()) { Animations = { animation } });
            return ring;
    }
    private void ShowSettings()
    {
        var panel = new StackPanel { Spacing = UiTheme.ControlRadius };
        panel.Children.Add(ActionButton("Personalizar meu som", () => { CloseSheet(); ShowMusicOnboarding(edit: true); return Task.CompletedTask; }));
        var region = new ComboBox { ItemsSource = new[] { "BR", "ZZ", "US", "JP", "KR", "PT", "GB", "DE", "FR", "MX", "AR" }, SelectedItem = _music.ChartRegion, MinHeight = 48 };
        region.SelectionChanged += (_, _) => { if (region.SelectedItem is string code) { _music.SetChartRegion(code); _ = _music.RefreshRecommendationsAsync(true); } };
        panel.Children.Add(Text("Região dos rankings · ZZ = mundo", UiTheme.Caption, Muted)); panel.Children.Add(region);
        panel.Children.Add(ActionButton("Limpar feedback Não tenho interesse", () => { _music.ResetRecommendationFeedback(); return _music.RefreshRecommendationsAsync(true); }));
        panel.Children.Add(ActionButton("Histórico de escuta", () =>
        {
            var history = new StackPanel { Spacing = 8 };
            foreach (var track in _music.History)
            {
                history.Children.Add(TrackRow(track));
                if (_music.Listening.TryGetValue(MusicController.SongKey(track), out var stat))
                    history.Children.Add(Text($"{stat.Plays} inícios · {Time(stat.Seconds)} ouvidos · {stat.Completed} conclusões · {stat.Skips} pulos rápidos", UiTheme.Caption, Muted));
            }
            ShowSheet("Histórico local", new ScrollViewer { Content = history, MaxHeight = Bounds.Height * .6 }); return Task.CompletedTask;
        }));
        if (OperatingSystem.IsAndroid()) panel.Children.Add(ActionButton("Discord por token (não oficial)", ShowDiscordAccountAsync));
        var cookies = System.IO.Path.Combine(_music.DataDirectory, "youtube-cookies.txt");
        if (System.IO.File.Exists(cookies)) panel.Children.Add(ActionButton("Apagar cookies antigos (não utilizados)", () => { System.IO.File.Delete(cookies); ShowSettings(); return Task.CompletedTask; }));
        if (DeveloperFeatures.Enabled)
        {
            panel.Children.Add(ActionButton("Diagnóstico de reprodução", () => { ShowDiagnostics(); return Task.CompletedTask; }));
            panel.Children.Add(ActionButton("Laboratório experimental", () => { ShowExperimental(); return Task.CompletedTask; }));
        }
        ShowSheet("Configurações", panel);
    }
    private YouTubeAccountLibrary? _account;
    private DiscordAccountPresence? _discordAccount;
    private Task ShowDiscordAccountAsync()
    {
        var panel = new StackPanel { Spacing = 12 };
        var warning = Text("Token pessoal dá acesso à conta. Self-bot viola regras do Discord e pode causar suspensão. Token só nesta sessão, sem salvar. Capa pública e progresso dependem do Discord; capas locais não são enviadas. Android pode suspender conexão em segundo plano.", UiTheme.Caption, Muted);
        warning.TextWrapping = TextWrapping.Wrap; panel.Children.Add(warning);
        var status = Text(_discordAccount?.Status ?? "Desconectado", UiTheme.Caption);
        status.TextWrapping = TextWrapping.Wrap; panel.Children.Add(status);
        var token = UiTheme.Field("Token pessoal Discord"); token.PasswordChar = '●'; panel.Children.Add(token);
        panel.Children.Add(ActionButton("Conectar", async () =>
        {
            if (!token.IsEnabled) return;
            token.IsEnabled = false; status.Text = "Conectando; aguardando confirmação do Discord…";
            var value = token.Text ?? ""; token.Text = "";
            _discordAccount?.Dispose(); _discordAccount = new DiscordAccountPresence(_music);
            try { await _discordAccount.ConnectAsync(value); CloseSheet(); }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { value = ""; token.IsEnabled = true; }
        }));
        panel.Children.Add(ActionButton("Desconectar", () => { _discordAccount?.Dispose(); _discordAccount = null; CloseSheet(); return Task.CompletedTask; }));
        ShowSheet("Discord não oficial", panel); return Task.CompletedTask;
    }
    private async Task ShowAccountAsync()
    {
        if (_account is null) { ShowGoogleWelcome(); return; }
        var panel = new StackPanel { Spacing = UiTheme.CompactGap };
        var supported = AppServices.ConnectYouTubeAccount is not null;
        var note = Text(supported
            ? "API oficial: playlists e vídeos curtidos, somente leitura. Não inclui feed, histórico ou catálogo completo do YT Music. Autorize acesso na tela do Google; token fica só na memória e pode expirar. yt-dlp não recebe credenciais. Até 1000 entradas por lista."
            : "Login Android ainda indisponível: exige autorização nativa Google e configuração do pacote/assinatura. Não usamos fluxo Desktop no Android.", UiTheme.Caption, Muted);
        note.TextWrapping = TextWrapping.Wrap; panel.Children.Add(note);
        var configPath = System.IO.Path.Combine(_music.DataDirectory, "google-oauth-desktop.json");
        if (System.IO.File.Exists(configPath)) panel.Children.Add(ActionButton("Apagar configuração local e desconectar", async () =>
        { System.IO.File.Delete(configPath); DisconnectAccount(); await ShowAccountAsync(); }));
        if (_account is { } account)
        {
            panel.Children.Add(ActionButton("Desconectar conta", async () => { DisconnectAccount(); await ShowAccountAsync(); }));
            panel.Children.Add(ActionButton("Vídeos curtidos", () => ShowAccountTracksAsync(account, null, "Vídeos curtidos")));
            foreach (var playlist in await account.PlaylistsAsync(CancellationToken.None))
                panel.Children.Add(ActionButton(playlist.Title, () => ShowAccountTracksAsync(account, playlist.Id, playlist.Title)));
        }
        ShowSheet("Biblioteca YouTube", panel);
    }
    private async Task ShowAccountTracksAsync(YouTubeAccountLibrary account, string? id, string title)
    {
        var tracks = await account.TracksAsync(id, CancellationToken.None);
        OpenCollection(title, tracks);
    }
    private CancellationTokenSource? _download;
    private void ShowExperimental()
    {
        if (!DeveloperFeatures.Enabled) return;
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
        panel.Children.Add(ActionButton("Histórico neste aparelho", () =>
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
    private void ShowDiagnostics()
    {
        if (!DeveloperFeatures.Enabled) return;
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
        if (DeveloperFeatures.Enabled) panel.Children.Add(ActionButton("Abrir diagnóstico", () => { ShowDiagnostics(); return Task.CompletedTask; }));
        ShowSheet("Informações do áudio", panel);
    }
    private void ShowSheet(string title, Control content)
    {
        if (_overlay is not null) _root.Children.Remove(_overlay);
        var layout = new Grid { RowDefinitions = new("Auto,*,Auto"), RowSpacing = UiTheme.CardGap };
        var label = UiTheme.Text(title, UiTheme.DialogTitle, weight: FontWeight.SemiBold); label.TextWrapping = TextWrapping.Wrap; layout.Children.Add(label);
        Put(layout, UiTheme.FadingScroll(content), 1, 0);
        Put(layout, ActionButton("Fechar", () => { CloseSheet(); return Task.CompletedTask; }), 2, 0);
        var sheet = UiTheme.Card(layout); sheet.MaxWidth = UiTheme.DialogMaxWidth; sheet.Margin = UiTheme.DialogMargin;
        sheet.HorizontalAlignment = HorizontalAlignment.Stretch; sheet.VerticalAlignment = VerticalAlignment.Center;
        sheet.MaxHeight = Math.Max(UiTheme.TouchSize, Bounds.Height - UiTheme.DialogMargin.Top - UiTheme.DialogMargin.Bottom);
        _overlay = new Border { Background = UiTheme.Brush(UiTheme.Scrim), Child = sheet };
        _overlay.PointerPressed += (_, e) => { if (ReferenceEquals(e.Source, _overlay)) { CloseSheet(); e.Handled = true; } };
        Grid.SetColumnSpan(_overlay, _root.ColumnDefinitions.Count); Grid.SetRowSpan(_overlay, 3); _root.Children.Add(_overlay); Animate(sheet);
    }
    private void CloseSheet()
    {
        if (_overlay is not null) _root.Children.Remove(_overlay); _overlay = null;
        if (!_showingPlayer) { _detailTrack = null; _detailSeek = null; _detailPlay = null; _detailTime = null; _detailDuration = null; _seeking = false; }
    }
    private static string Time(double seconds) => TrackTextFormatter.Duration(seconds);
    private static Control ToggleIcon(string name, bool active, bool repeatOne = false)
    {
        var panel = new Grid { Width = UiTheme.IconSize, Height = UiTheme.ToggleIconHeight };
        var icon = new Grid(); icon.Children.Add(Icons.Make(name, active ? Accent : Muted));
        if (name == "repeat" && repeatOne)
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
