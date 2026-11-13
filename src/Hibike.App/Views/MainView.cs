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
    private readonly System.Collections.ObjectModel.ObservableCollection<Control> _homeSections = [];
    private readonly ItemsControl _home = new()
    {
        ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel { CacheLength = .25 })
    };
    private ScrollViewer _homeScroll = null!;
    private IReadOnlyList<AccountPlaylist> _accountPlaylists = [];
    private IReadOnlyList<Track> _accountLikes = [];
    private string? _homeFingerprint;
    private bool _accountLoading;
    private readonly TextBlock _pageTitle = TopBarTitle("Início");
    private readonly TextBox _search = UiTheme.Field("Música, artista ou link");
    private readonly TextBlock _status = Text("", UiTheme.Caption, Muted);
    private readonly Border _statusBox;
    private readonly Button _clearSearch;
    private readonly Border _mini;
    private const double MiniCoverSize = 72;
    private readonly ContentControl _nowArt = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly TextBlock _nowTitle = Text("", UiTheme.Body);
    private readonly TextBlock _nowArtist = Text("", UiTheme.Caption, Muted);
    private readonly Button _play;
    private readonly ProgressBar _progress = new() { Height = UiTheme.MiniProgressHeight, Minimum = 0, Maximum = 1 };
    private readonly Border _bottom;
    private readonly List<Button> _nav = [];
    private Border? _overlay;
    private Border? _playerPage;
    private TextBlock? _expandedRecoveryNotice;
    private readonly TextBlock _miniRecoveryNotice = Text("", UiTheme.Caption, UiTheme.Warning);
    private readonly Dictionary<string, Border> _rows = new();
    private readonly Dictionary<string, TextBlock> _rowTitles = new();
    private readonly Dictionary<string, Equalizer> _equalizers = new();
    private bool _showingPlayer;
    private bool _compactPlayer;
    private Slider? _detailSeek;
    private TextBlock? _detailTime;
    private TextBlock? _detailDuration;
    private Button? _detailPlay;
    private bool _seeking;
    private Track? _shownTrack;
    private string _page = "home";
    private const string Muted = UiTheme.TextSecondary, Accent = UiTheme.Primary;

    public MainView(MusicController music) : this(music, OperatingSystem.IsAndroid()) { }

    internal MainView(MusicController music, bool androidLayout)
    {
        _androidLayout = androidLayout;
        _music = music; Background = UiTheme.Brush(UiTheme.Background);
        TopLevel.SetAutoSafeAreaPadding(this, true);
        _sidebar = BuildSidebar(); _root.Children.Add(_sidebar); Grid.SetRowSpan(_sidebar, 3);
        _main = new Border { Padding = UiTheme.ContentPadding, HorizontalAlignment = HorizontalAlignment.Stretch }; Put(_root, _main, 0, 1);
        var content = new Grid { RowDefinitions = new("Auto,Auto,*"), RowSpacing = 4 }; _main.Child = content;
        var header = new Grid { Name = "AppHeader", RowDefinitions = new("Auto,20"), ColumnDefinitions = new("Auto,*,Auto") };
        header.RowDefinitions[0].MinHeight = 28;
        var brandIcon = _brandIcon = new Image { Source = new Avalonia.Media.Imaging.Bitmap(Avalonia.Platform.AssetLoader.Open(new Uri("avares://Hibike.App/Assets/hibike-desktop.png"))), Width = 22, Height = 22, VerticalAlignment = VerticalAlignment.Center };
        var pageBrand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        pageBrand.Children.Add(brandIcon); pageBrand.Children.Add(_pageTitle); Put(header, pageBrand, 0, 1);
        _preferencesBack = CollectionIcon("back", "Voltar", () => { BackFromPreferences(); return Task.CompletedTask; });
        _preferencesBack.IsVisible = false;
        _preferencesBack.Margin = new Thickness(0, 0, UiTheme.CompactGap, 0);
        Put(header, _preferencesBack, 0, 0);
        Grid.SetRowSpan(_preferencesBack, 2);
        var androidMenu = _profileButton = IconButton("account-off", "Perfil e menu do aplicativo", () => { ToggleTopMenu(); return Task.CompletedTask; });
        androidMenu.MinHeight = androidMenu.MinWidth = UiTheme.TouchSize;
        androidMenu.Height = androidMenu.Width = androidMenu.MinHeight;
        androidMenu.Padding = UiTheme.Zero;
        if (!OperatingSystem.IsAndroid()) androidMenu.MinHeight = androidMenu.Height = 32;
        androidMenu.Margin = new Thickness(UiTheme.CompactGap, 0, 0, 0);
        Put(header, androidMenu, 0, 2); Grid.SetRowSpan(androidMenu, 2);
        if (OperatingSystem.IsAndroid()) { brandIcon.IsVisible = true; _pageTitle.Text = "Hibike"; }
        pageBrand.VerticalAlignment = VerticalAlignment.Center; _pageTitle.VerticalAlignment = VerticalAlignment.Center;
        InitializeLoading(header, pageBrand);
        content.Children.Add(header);
        AddSearchIcon(_search);
        TextInputOptions.SetContentType(_search, TextInputContentType.Search);
        TextInputOptions.SetReturnKeyType(_search, TextInputReturnKeyType.Search);
        _clearSearch = IconButton("close", "Limpar busca", () => { _search.Text = ""; _search.Focus(); return Task.CompletedTask; });
        _clearSearch.Classes.Add("subtle"); _clearSearch.IsVisible = false;
        _search.InnerRightContent = _clearSearch;
        _search.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await SearchAsync(); } };
        _home.ItemsSource = _homeSections;
        _homeScroll = UiTheme.FadingScroll(_home);
        InitializeAccountRefresh();
        Put(content, _homeRefreshContainer!, 2, 0);
        var statusRow = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = UiTheme.CompactGap };
        _status.TextWrapping = TextWrapping.Wrap; _status.MaxLines = 2; _status.VerticalAlignment = VerticalAlignment.Center; statusRow.Children.Add(_status);
        if (DeveloperFeatures.Enabled) Put(statusRow, IconButton("info", "Ver diagnóstico", () => { ShowDiagnostics(); return Task.CompletedTask; }), 0, 1);
        _statusBox = new Border { Child = statusRow }; UiTheme.StateBanner(_statusBox, UiTheme.Warning); var statusArea = new StackPanel { Children = { _statusBox } }; Put(content, statusArea, 1, 0);
        _play = IconButton("play", "Reproduzir, pausar ou cancelar", _music.ToggleAsync, UiTheme.OnPrimary); _play.Classes.Add("primary"); _play.Classes.Add("miniPlay");
        _mini = BuildMiniPlayer(); Put(_root, _mini, 1, 1);
        InitializePlayerSwipe();
        _mini.SizeChanged += (_, _) => RefreshMiniPlayerInset();
        InitializeWorkspace(content);
        InitializeOfflineWorkspace(content, statusArea);
        InitializeTopMenu(content);
        InitializeCollapsingTopBars(content);
        var navGrid = new Grid { ColumnDefinitions = new("*,*,*,*,*"), Height = OperatingSystem.IsAndroid() ? 60 : UiTheme.NavHeight, Background = UiTheme.Brush(UiTheme.Surface) };
        var tabs = new[] { ("home", "Início", "home"), ("explore", "Buscar", "search"), ("library", "Biblioteca", "library"), ("downloads", "Downloads", "download"), ("queue", "Fila", "queue") };
        for (var index = 0; index < tabs.Length; index++)
        {
            var (key, label, icon) = tabs[index];
            var button = NavButton(key, label, icon, true); Put(navGrid, button, 0, index);
        }
        _bottom = new Border { Child = navGrid, BorderBrush = UiTheme.Brush(UiTheme.Outline), BorderThickness = UiTheme.TopBorder };
        Put(_root, _bottom, 2, 1); Content = _root;
        SizeChanged += (_, _) =>
        {
            var compact = _androidLayout || Bounds.Width < UiTheme.DesktopWidth;
            _root.ColumnDefinitions[0].Width = new GridLength(compact ? 0 : UiTheme.SidebarWidth);
            _sidebar.IsVisible = !compact; _bottom.IsVisible = compact;
            brandIcon.IsVisible = _page == "home" && compact && !ShowOfflineHome;
            _pageTitle.IsVisible = _page != "home" || compact || ShowOfflineHome;
            _main.IsVisible = !_showingPlayer || _androidLayout;
            _bottom.IsVisible = compact && (!_showingPlayer || _androidLayout);
            _main.Padding = _androidLayout ? UiTheme.MobileContentPadding
                : compact ? new Thickness(12, 0, 12, 4) : new Thickness(32, 4, 32, 12);
            _homeScroll.Margin = _androidLayout ? new Thickness(-UiTheme.MobileGutter, 0) : UiTheme.Zero;
            if (_androidLayout) _mini.Margin = UiTheme.MobileMiniMargin;
            if (_page == "home" && !ShowOfflineHome) RefreshHome();
            RefreshWorkspace();
            RefreshCollectionPages();
            RefreshMiniPlayerInset();
            // Title and two 48dp actions fit at 360dp; no artificial second toolbar row.
        };
        if (OperatingSystem.IsAndroid() && AppServices.RequestDisplayFrame is { } requestFrame)
            SynchronizeHomeInertia(requestFrame);
        AppServices.BackHandler = HandleBack; AppServices.OpenPlayerView = ShowPlayer;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = HandleBack(); }
            else if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                if (_showingPlayer) ClosePlayer();
                _page = "explore";
                Refresh();
                _search.Focus(); _search.SelectAll(); e.Handled = true;
            }
        };
        AddHandler(KeyDownEvent, async (_, e) =>
        {
            if (e.Key == Key.F5 && e.KeyModifiers == KeyModifiers.None && TopLevel.GetTopLevel(this) is Window { IsActive: true } && _googleWelcome is null && _onboarding is null)
            { e.Handled = true; await RefreshHomeAsync(); }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnPlaybackKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnPlaybackKeyUp, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        InitializeDiscordRecovery();
        AttachedToVisualTree += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is Window desktopWindow) { desktopWindow.Deactivated -= ResetPlaybackKey; desktopWindow.Deactivated += ResetPlaybackKey; }
            _music.Changed -= Refresh; _music.Tick -= RefreshPlayback; _music.Tick -= RefreshDock;
            _music.Changed += Refresh; _music.Tick += RefreshPlayback; _music.Tick += RefreshDock;
            if (_music.Player is IAudioPlaybackStateNotifications notifications)
            {
                notifications.PlaybackStateChanged -= OnPlaybackStateChanged;
                notifications.PlaybackStateChanged += OnPlaybackStateChanged;
            }
            _music.StartObserving(); Refresh();
            StartDiscordRecovery();
            await _music.InitializeAsync();
            if (_music.CanUseOnline)
                await WithHeaderLoadingAsync("Restaurando conta Google…", RestoreAccountAsync);
            _accountRefreshTimer.Start();
            if (!_music.OnboardingCompleted && _music.CanUseOnline) ShowGoogleWelcome(firstRun: true);
            if (_music.OnboardingCompleted) _ = _music.RefreshRecommendationsAsync();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            StopDiscordRecovery();
            _accountRefreshTimer.Stop();
            _music.Changed -= Refresh;
            _music.Tick -= RefreshPlayback;
            _music.Tick -= RefreshDock;
            if (_music.Player is IAudioPlaybackStateNotifications notifications)
                notifications.PlaybackStateChanged -= OnPlaybackStateChanged;
            _music.StopObserving();
        };
        Refresh();
    }
    private Border BuildSidebar()
    {
        var panel = new StackPanel { Spacing = 8, Margin = new Thickness(8, 24) };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 16) };
        brand.Children.Add(new Image { Source = new Avalonia.Media.Imaging.Bitmap(Avalonia.Platform.AssetLoader.Open(new Uri("avares://Hibike.App/Assets/hibike-desktop.png"))), Width = 22, Height = 22 });
        brand.Children.Add(UiTheme.Text("Hibike", 16, UiTheme.TextPrimary, FontWeight.SemiBold));
        panel.Children.Add(brand);
        panel.Children.Add(NavButton("home", "Início", "home", false));
        panel.Children.Add(NavButton("explore", "Buscar", "search", false));
        panel.Children.Add(NavButton("library", "Biblioteca", "library", false));
        panel.Children.Add(NavButton("downloads", "Downloads", "download", false));
        panel.Children.Add(NavButton("queue", "Fila", "queue", false));
        panel.Children.Add(new Border { Height = 1, Background = UiTheme.Brush(UiTheme.Outline), Margin = new Thickness(8, 18) });
        var import = SidebarAction("folder", "Importar músicas", OpenLocalAsync);
        import.HorizontalAlignment = HorizontalAlignment.Stretch;
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
            ClosePlayer(); CloseTopMenu(); CloseSheet();
            RevealMobileTopBars();
            if (key == "library" && _page == "library") _mobileLibraryGroup = null;
            if (key == "queue") _queueOpen = false;
            _page = key;
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
        const double contentInset = 12, progressInset = 8;
        var grid = new Grid { RowDefinitions = new($"{MiniCoverSize},Auto"), ColumnDefinitions = new($"{MiniCoverSize},*,{UiTheme.TouchSize},{UiTheme.TouchSize}") };
        var open = ActionButton("", () => { ShowPlayer(); return Task.CompletedTask; });
        Avalonia.Automation.AutomationProperties.SetName(open, "Abrir player");
        open.Classes.Add("subtle");
        open.Padding = UiTheme.Zero;
        open.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        open.VerticalContentAlignment = VerticalAlignment.Stretch;
        var metadata = new Grid { ColumnDefinitions = new($"{MiniCoverSize},*") };
        _nowArt.Content = MiniCover(null);
        metadata.Children.Add(_nowArt);
        var info = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(contentInset, 0, UiTheme.CompactGap, progressInset) };
        _nowTitle.TextTrimming = _nowArtist.TextTrimming = TextTrimming.CharacterEllipsis;
        info.Children.Add(_nowTitle);
        info.Children.Add(_nowArtist);
        Put(metadata, info, 0, 1);
        open.Content = metadata;
        grid.Children.Add(open);
        Grid.SetColumnSpan(open, 2);
        var next = IconButton("next", "Próxima faixa", _music.NextAsync);
        next.Classes.Add("subtle");
        _play.Margin = next.Margin = new Thickness(0, 0, 0, progressInset);
        Put(grid, _play, 0, 2);
        Put(grid, next, 0, 3);
        _progress.VerticalAlignment = VerticalAlignment.Bottom;
        _progress.Margin = new Thickness(contentInset, 0, contentInset, progressInset);
        _progress.IsHitTestVisible = false;
        Put(grid, _progress, 0, 1);
        Grid.SetColumnSpan(_progress, 3);
        _miniRecoveryNotice.IsVisible = false;
        _miniRecoveryNotice.TextWrapping = TextWrapping.Wrap;
        _miniRecoveryNotice.MaxLines = 2;
        _miniRecoveryNotice.Margin = new Thickness(contentInset, 4, contentInset, progressInset);
        Put(grid, _miniRecoveryNotice, 1, 0);
        Grid.SetColumnSpan(_miniRecoveryNotice, 4);
        var card = UiTheme.Card(grid, padding: 0);
        card.Margin = UiTheme.MiniMargin;
        card.ClipToBounds = true;
        card.MaxWidth = UiTheme.PageMaxWidth - UiTheme.PageMargin.Left - UiTheme.PageMargin.Right;
        return card;
    }

    private static Artwork MiniCover(Track? track) => new(track, MiniCoverSize, highQuality: true)
    {
        BorderThickness = UiTheme.Zero,
        CornerRadius = new CornerRadius(UiTheme.CardRadius - 1, 0, 0, UiTheme.CardRadius - 1)
    };

    private async Task SearchAsync()
    {
        if (_page != "explore" || _mobileSearchPage is null)
        {
            _page = "explore";
            Refresh();
        }
        var query = _search.Text?.Trim() ?? "";
        if (!_music.CanUseOnline || _searchScope == "Sua biblioteca" || query.Length == 0)
        {
            Refresh();
            return;
        }
        if (Uri.TryCreate(query, UriKind.Absolute, out var link) && link.Scheme is "http" or "https"
            && (link.Host == "youtube.com" || link.Host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase))
            && link.AbsolutePath.TrimEnd('/') == "/playlist")
        {
            var id = link.Query.TrimStart('?').Split('&').FirstOrDefault(part => part.StartsWith("list=", StringComparison.Ordinal))?[5..];
            if (id is not null && System.Text.RegularExpressions.Regex.IsMatch(id, "^[A-Za-z0-9_-]{1,200}$"))
            {
                CancelExploreSearch();
                await ShowPagedCollectionAsync("Playlist do YouTube", "https://www.youtube.com/playlist?list=" + id,
                    new Track(id, "Playlist do YouTube", "YouTube", ""), "Playlist");
                return;
            }
        }
        if (_mobileSearchAttempt == query && (_music.Searching || _artistLookup is not null)) return;
        _mobileSearchAttempt = query;
        await Task.WhenAll(SearchArtistResultsAsync(query), _music.SearchAsync(query));
    }
    private async Task OpenLocalAsync()
    {
        ClosePlayer(); CloseTopMenu();
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
            try
            {
                foreach (var file in files)
                {
                    // Keep a private copy so the original and Android URI permissions are independent.
                    var fileName = file.Name;
                    var id = Guid.NewGuid().ToString("N");
                    var directory = Path.Combine(_music.DataDirectory, "audio");
                    Directory.CreateDirectory(directory);
                    var path = Path.Combine(directory, id + Path.GetExtension(fileName));
                    Track track;
                    try
                    {
                        await using (var input = await file.OpenReadAsync())
                        await using (var output = File.Create(path))
                            await input.CopyToAsync(output);
                        track = await Task.Run(() => AppServices.ReadLocalTrack(
                            new Track(id, Path.GetFileNameWithoutExtension(fileName), "Áudio local", path, IsLocal: true),
                            Path.Combine(_music.DataDirectory, "artwork")));
                        await _music.SaveOfflineTrackAsync(track);
                    }
                    catch
                    {
                        File.Delete(path);
                        throw;
                    }
                    _music.Enqueue(track);
                    await _music.ToggleFavoriteAsync(track);
                    first ??= track;
                }
            }
            finally { foreach (var file in files) file.Dispose(); }
            if (first is null) return;
            _page = "downloads";
            await _music.PlayAsync(first);
            Refresh();
        }
        catch (Exception ex) { _music.SetStatus("Não foi possível abrir o áudio: " + ex.Message); }
    }

    private string? _animatedPage;
    private bool _lastReducedMotion;
    private Action<Track>? _updatePlayerTrack;

    private void Refresh()
    {
        if (_lastReducedMotion != _music.ReduceMotion)
        {
            _lastReducedMotion = _music.ReduceMotion;
            if (_lastReducedMotion)
                foreach (var button in this.GetVisualDescendants().OfType<Button>())
                { button.Transitions = null; button.RenderTransform = null; }
        }
        if (_page != "collection" && _collectionBack.Count > 0)
        {
            _collectionBack.Clear();
            _collectionPage.Child = null;
            _collectionTracks = [];
        }
        RefreshHeaderLoading();
        RefreshProfile();
        _statusBox.IsVisible = !IsCollectionPage && _music.HasError && (_music.CanUseOnline || _music.Current?.IsLocal == true);
        _status.Text = _music.Status;
        foreach (var button in _nav)
        {
            var (key, label, icon, vertical) = ((string, string, string, bool))button.Tag!;
            var active = key == _page || !_androidLayout && key == "queue" && _queueOpen;
            if (button.Classes.Contains("selected") == active && button.Content is not string) continue;
            button.Classes.Set("selected", active);
            button.Background = Brushes.Transparent;
            button.CornerRadius = new(10); button.Padding = new Thickness(vertical ? 1 : 4, 4);
            button.MinHeight = vertical ? 56 : 76;
            var stack = new StackPanel { Spacing = vertical ? 2 : 6, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var image = Icons.Make(icon, active ? Accent : Muted, 22); image.HorizontalAlignment = HorizontalAlignment.Center;
            image.VerticalAlignment = VerticalAlignment.Center;
            var indicator = new Border { Child = image, Width = 52, Height = vertical ? 28 : 32,
                CornerRadius = new(16), Background = active ? UiTheme.Brush("#292929") : Brushes.Transparent };
            stack.Children.Add(indicator);
            var caption = UiTheme.Text(label, UiTheme.Label, active ? UiTheme.TextPrimary : Muted, active ? FontWeight.SemiBold : FontWeight.Normal);
            caption.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(caption);
            var wasBuilt = button.Content is not string;
            button.Content = stack;
            if (active && wasBuilt && button.IsEffectivelyVisible) Animate(indicator, 2);
        }
        var compactHeader = _androidLayout || Bounds.Width < UiTheme.DesktopWidth;
        _brandIcon.IsVisible = _page == "home" && compactHeader && !ShowOfflineHome;
        _pageTitle.IsVisible = _page != "home" || compactHeader || ShowOfflineHome;
        _pageTitle.Text = _page switch { "home" when ShowOfflineHome => _music.OfflineMode ? "Modo offline" : "Sem conexão", "home" => "Hibike", "downloads" => "Downloads", "settings" => "Configurações", "account" => "Minha conta", "collection" => "Coleção", _ => "Buscar" };
        _pageTitle.FontSize = _page == "downloads" ? UiTheme.PageTitle : UiTheme.TopBarTitle;
        var showOnlineHome = _page == "home" && !ShowOfflineHome;
        _homeScroll.IsVisible = showOnlineHome;
        if (_homeRefreshContainer is not null) _homeRefreshContainer.IsVisible = showOnlineHome;
        RefreshOfflineWorkspace();
        var preferencesOpen = _page is "settings" or "account";
        _preferencesBack.IsVisible = preferencesOpen;
        _profileButton!.IsVisible = !preferencesOpen;
        if (_preferencesPage is not null) _preferencesPage.IsVisible = preferencesOpen;
        if (_page == "home" && !ShowOfflineHome) RefreshHome();
        RefreshWorkspace();
        var displayed = _music.Pending ?? _music.Current;
        UpdateRows(displayed);
        if (!Equals(displayed, _shownTrack)) { _shownTrack = displayed; _nowArt.Content = MiniCover(displayed); }
        _nowTitle.Text = TrackTextFormatter.Title(displayed?.Title, displayed?.Artist);
        _nowArtist.Text = TrackTextFormatter.Artist(displayed?.Artist);
        if (displayed is not null) _updatePlayerTrack?.Invoke(displayed);
        RefreshPlayerQueue();
        RefreshPlayback();
        RefreshDiscovery();
        RefreshCollectionPages();
        var visualPage = ShowOfflineHome ? "offline" : _page;
        RefreshMiniPlayerInset();
        if (_animatedPage != visualPage)
        {
            RevealMobileTopBars();
            var previous = _animatedPage;
            _animatedPage = visualPage;
            var page = visualPage switch
            {
                "home" => (Control?)_homeRefreshContainer,
                "offline" or "downloads" => _downloadsPage,
                "explore" => _mobileSearchPage,
                "library" => _mobileLibrary,
                "queue" => _mobileQueue,
                "collection" => _collectionPage,
                _ => _preferencesPage
            };
            if (previous is not null && page is not null && !_showingPlayer)
                Animate(page, 8);
        }
    }
    private double MobileHomeCardSize => Math.Clamp(Math.Floor((Bounds.Width - 2 * UiTheme.MobileGutter - UiTheme.CompactGap) / 2), 120, 212);

    private sealed class HomeShelfPosition { public double X; }
    private sealed record HomeCard(Track Track, Func<Control> Create);
    private static HomeCard Card(Track track, Func<Control> create) => new(track, create);
    private void RefreshHome()
    {
        var fingerprint = string.Join("|", _music.History.Select(t => t.Source)) + ":" + string.Join("|", _music.Favorites.Select(t => t.Source))
            + ":" + string.Join("|", _music.Pins.Select(t => t.Source)) + ":" + string.Join("|", _music.Playlists.Select(p => p.Name + p.Tracks.Count))
            + ":" + (_account is not null) + ":" + _accountLoading + ":" + _accountError + ":" + string.Join("|", _accountLikes.Select(t => t.Id))
            + ":" + string.Join("|", _accountPlaylists.Select(p => p.Id)) + ":" + _music.Current?.Source + ":" + (Bounds.Width < UiTheme.DesktopWidth)
            + ":" + string.Join("|", _music.Queue.Select(t => t.Source))
            + ":" + string.Join("|", _music.FavoriteArtists.Select(a => a.Id)) + ":" + string.Join("|", _music.HomeRecommendations.Select(t => t.Source))
            + ":" + (_androidLayout ? MobileHomeCardSize : 0) + ":" + _music.HomeLoading + ":" + _music.HomeMessage + ":" + _music.RegionalChart?.RetrievedUtc + ":" + _music.GlobalChart?.RetrievedUtc + ":" + _music.TrendingChart?.RetrievedUtc;
        if (fingerprint == _homeFingerprint) return;
        _homeFingerprint = fingerprint;
        var previous = _homeSections.ToArray();
        var next = new List<Control>();
        if (_accountExpired && _accountError is { } accountError)
        {
            var message = Text(accountError, UiTheme.Caption, UiTheme.Error); message.TextWrapping = TextWrapping.Wrap; next.Add(message);
            next.Add(ActionButton("Entrar novamente com Google", ShowAccountAsync));
        }
        var shownRecommendations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Task OpenHistory()
        {
            OpenLocalCollection("Histórico", _music.History);
            return Task.CompletedTask;
        }

        Shelf("Continuar ouvindo", "",
            _music.Pins.Concat(_music.History).DistinctBy(t => t.Source).Take(20)
                .Select(t => Card(t, () => CoverCard(t, TrackTextFormatter.Artist(t.Artist), async () =>
                {
                    if (_music.Current?.Source == t.Source) await _music.ToggleAsync();
                    else await _music.PlayAsync(t);
                }))),
            _music.History.Count > 0 ? OpenHistory : null);
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
        if (_music.HomeLoading || !string.IsNullOrEmpty(_music.HomeMessage)) next.Add(Text(_music.HomeLoading ? "Preparando descobertas… Sua biblioteca já está disponível." : _music.HomeMessage, UiTheme.Caption, Muted));
        var recommended = _music.Queue.Where(_music.IsRecommended).Take(12).ToArray();
        if (recommended.Length > 0) Shelf("Porque você ouviu…", _music.PlaybackContext, recommended.Select(t => Card(t, () => TrackCard(t))));
        if (_music.History.Count >= 5)
            Shelf("Seus mixes", "Seleções do seu histórico", _music.History.GroupBy(t => t.Artist).Where(g => g.Count() >= 2).Take(4).Select(g => Card(g.First(), () => CoverCard(g.First() with { Title = "Mix de " + g.Key }, TrackCount(g.Count()), () => { OpenCollection("Mix de " + g.Key, g.ToArray(), "Mix · histórico"); return Task.CompletedTask; }, songActions: false))));
        if (_music.History.Count == 0) next.Add(EmptyState("play", "Sua trilha começa aqui", "Suas últimas músicas aparecem aqui automaticamente. Comece com uma busca ou traga seus arquivos.", "Buscar música", () => { _page = "explore"; Refresh(); _search.Focus(); return Task.CompletedTask; }));
        if (_account is not null)
        {
            Shelf("Suas curtidas no YouTube", "Biblioteca da conta · não são recomendações", _accountLikes.Take(30).Select(t => Card(t, () => TrackCard(t))));
            Shelf("Suas playlists", "Playlists da conta YouTube", _accountPlaylists.Take(50).Select(p =>
            {
                var art = new Track(p.Id, p.Title, "Playlist", "https://www.youtube.com/playlist?list=" + Uri.EscapeDataString(p.Id), Thumbnail: p.Thumbnail);
                return Card(art, () => CoverCard(art, "Playlist", () => ShowAccountTracksAsync(_account!, p.Id, p.Title), playlist: true));
            }));
        }
        if (!_androidLayout && Bounds.Width >= UiTheme.DesktopWidth && _music.Queue.Count > 0)
            Shelf("A seguir", "Próximas músicas da sua fila", _music.Upcoming.Take(20).Select(t => Card(t, () => TrackCard(t))));
        Shelf("Suas músicas favoritas", "Favoritos e arquivos salvos no Hibike", _music.Favorites.OrderByDescending(t => _music.Listening.GetValueOrDefault(MusicController.SongKey(t))?.LastPlayed).Take(30).Select(t => Card(t, () => TrackCard(t))));
        if (_music.Favorites.Count == 0) next.Add(ActionButton("Importar músicas para sua coleção", OpenLocalAsync));
        if (_androidLayout)
            foreach (var control in next.Where(control => control.Tag is not ValueTuple<string, string>))
                control.Margin = new Thickness(UiTheme.MobileGutter, control.Margin.Top, UiTheme.MobileGutter, control.Margin.Bottom);
        var spacing = _androidLayout || Bounds.Width < UiTheme.DesktopWidth ? 8 : UiTheme.SectionGap;
        for (var i = 0; i < next.Count; i++)
        {
            var margin = next[i].Margin;
            next[i].Margin = new Thickness(margin.Left, margin.Top, margin.Right, i + 1 < next.Count ? spacing : 0);
        }
        foreach (var removed in previous.Where(c => !next.Contains(c))) _homeSections.Remove(removed);
        for (var i = 0; i < next.Count; i++)
        {
            var item = next[i];
            var index = _homeSections.IndexOf(item);
            if (index == i) continue;
            if (index >= 0) _homeSections.Move(index, i);
            else _homeSections.Insert(i, item);
        }

        void Shelf(string title, string subtitle, IEnumerable<HomeCard> cards, Func<Task>? headerAction = null)
        {
            var personalized = title == "Feito para você" || title == "Descubra algo novo" || title.StartsWith("Porque você gosta de ") || title == "Tendências para você";
            var cardList = cards.Where(c => !personalized || shownRecommendations.Add(c.Track.Source)).ToArray();
            if (cardList.Length == 0) return;
            var key = title + ":" + subtitle + ":" + (_androidLayout ? MobileHomeCardSize : 0) + ":" + (Bounds.Width < UiTheme.DesktopWidth) + ":" + string.Join("|", cardList.Select(c => c.Track.ToString()));
            var old = previous.OfType<StackPanel>().FirstOrDefault(c => c.Tag is ValueTuple<string, string> tag && tag.Item1 == title);
            if (old?.Tag is ValueTuple<string, string> oldTag && oldTag.Item2 == key) { next.Add(old); return; }
            var section = new StackPanel { Spacing = 6, Margin = new Thickness(0, Bounds.Width < UiTheme.DesktopWidth ? 6 : 14, 0, 0) };
            section.Tag = (title, key);
            var header = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 };
            var heading = Text(title, UiTheme.DialogTitle); heading.FontWeight = FontWeight.SemiBold; heading.TextTrimming = TextTrimming.CharacterEllipsis; header.Children.Add(heading);
            var scroll = UiTheme.FadingScroll(horizontal: true);
            if (_androidLayout)
            {
                header.Margin = new Thickness(UiTheme.MobileGutter, 0);
                if (headerAction is not null)
                {
                    var history = ActionButton("Histórico", headerAction);
                    history.Classes.Add("subtle");
                    Put(header, history, 0, 1);
                }
            }
            else
            {
                var controls = new StackPanel { Orientation = Orientation.Horizontal };
                if (headerAction is not null)
                {
                    var history = ActionButton("Histórico", headerAction);
                    history.Classes.Add("subtle");
                    controls.Children.Add(history);
                }
                if (cardList.Length > 1)
                {
                    var step = UiMotion.HorizontalScrollSteps(scroll);
                    controls.Children.Add(IconButton("back", "Voltar em " + title, () => { step(-1); return Task.CompletedTask; }));
                    controls.Children.Add(IconButton("forward", "Avançar em " + title, () => { step(1); return Task.CompletedTask; }));
                }
                Put(header, controls, 0, 1);
            }
            section.Children.Add(header);
            if (subtitle.Length > 0)
            {
                var note = Text(subtitle, UiTheme.Caption, Muted);
                note.TextWrapping = TextWrapping.Wrap;
                if (_androidLayout) note.Margin = new Thickness(UiTheme.MobileGutter, 0);
                section.Children.Add(note);
            }
            var row = new ItemsControl
            {
                ItemsSource = cardList,
                Margin = _androidLayout ? new Thickness(UiTheme.MobileGutter, 0, 0, 0) : UiTheme.Zero,
                ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel
                { Orientation = Orientation.Horizontal, CacheLength = .5 }),
                ItemTemplate = new FuncDataTemplate<HomeCard>((card, _) =>
                {
                    if (card is null) return new Border();
                    var control = card.Create();
                    control.Margin = new Thickness(0, 0, _androidLayout ? UiTheme.CompactGap : UiTheme.PageGap, 0);
                    return control;
                })
            };
            scroll.Content = row; section.Children.Add(scroll);
            var position = (old?.Children.LastOrDefault() as ScrollViewer)?.Tag as HomeShelfPosition ?? new HomeShelfPosition();
            scroll.Tag = position;
            var ready = false;
            scroll.AttachedToVisualTree += (_, _) =>
            {
                ready = false;
                Dispatcher.UIThread.Post(() =>
                {
                    if (TopLevel.GetTopLevel(scroll) is null) return;
                    scroll.Offset = new Vector(position.X, 0);
                    ready = true;
                }, DispatcherPriority.Loaded);
            };
            scroll.DetachedFromVisualTree += (_, _) => ready = false;
            scroll.ScrollChanged += (_, e) =>
            {
                if (ready && e.OffsetDelta.X != 0 && scroll.Viewport.Width > 0 && scroll.Extent.Width > scroll.Viewport.Width)
                    position.X = scroll.Offset.X;
            };
            next.Add(section);
        }
    }
    private Control TrackCard(Track track)
    {
        return CoverCard(track, TrackTextFormatter.Artist(track.Artist), async () => { await _music.PlayAsync(track); });
    }
    private Control CoverCard(Track track, string subtitle, Func<Task> action, bool playlist = false, bool songActions = true, double? cardSize = null, bool roundArtwork = false)
    {
        if (track.IsArtist)
        {
            var known = _music.FavoriteArtists.FirstOrDefault(a => track.Source.TrimEnd('/').EndsWith("/" + a.Id, StringComparison.Ordinal));
            if (track.Thumbnail is null && known?.Image is not null) track = track with { Thumbnail = known.Image };
            songActions = false;
            action = () => ShowChannelAsync(track);
            subtitle = "Artista";
        }
        var size = cardSize ?? (_androidLayout && _page == "home" ? MobileHomeCardSize
            : Bounds.Width < UiTheme.DesktopWidth ? (playlist ? 168 : 160) : (playlist ? 204 : 196));
        var panel = new StackPanel { Width = size, Spacing = 4 };
        var cover = new Grid { Width = size, Height = size };
        var artwork = new Artwork(track, size, highQuality: true)
        {
            DeferOffscreen = _page == "home", CornerRadius = new(roundArtwork ? size / 2 : 16),
            Background = _page == "library" ? UiTheme.Brush(UiTheme.SurfaceElevated) : Brushes.Transparent,
            BorderThickness = new Thickness(0)
        };
        cover.Children.Add(artwork);
        var playOverlay = new Button { Content = Icons.Make("play"), IsHitTestVisible = false, Focusable = false,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 60, 8), IsVisible = false };
        playOverlay.Classes.Add("cardAction");
        if (songActions && !playlist) cover.Children.Add(playOverlay);
        panel.Children.Add(cover);
        var title = UiTheme.Text(TrackTextFormatter.Title(track.Title, track.Artist), UiTheme.Body,
            weight: _page == "library" ? FontWeight.SemiBold : FontWeight.Normal);
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
        void SetActions(bool visible)
        {
            var show = songActions && !playlist && !OperatingSystem.IsAndroid() && visible;
            var entering = show && !more.IsVisible;
            playOverlay.IsVisible = more.IsVisible = show;
            if (entering)
            {
                UiMotion.Enter(playOverlay, 4, decorative: true);
                UiMotion.Enter(more, 4);
            }
        }
        host.PointerEntered += (_, _) => SetActions(true);
        host.PointerExited += (_, _) => SetActions(host.IsKeyboardFocusWithin);
        host.GotFocus += (_, _) => SetActions(true);
        host.LostFocus += (_, _) => SetActions(host.IsPointerOver);
        if (songActions) AttachSongHold(button, track);
        return host;
    }
    private async Task LoadAccountHomeAsync()
    {
        if (!_music.CanUseOnline || _account is not { } account || _accountLoading) return;
        _accountLoading = true; Refresh();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            _accountNetworkRequest = timeout;
            await account.ValidateSessionAsync(timeout.Token);
            var playlists = await account.PlaylistsAsync(timeout.Token);
            var likes = await account.TracksAsync(null, timeout.Token);
            if (!ReferenceEquals(_account, account) || !_music.CanUseOnline) return;
            _accountPlaylists = playlists; _accountLikes = likes;
            _accountExpired = false; _accountError = null; _profileAccount = null;
            PlaybackDiagnostics.Write(DiagnosticSector.Google, "Biblioteca", "Playlists e curtidas atualizadas.");
        }
        catch (AccountSessionExpiredException)
        {
            if (!ReferenceEquals(_account, account)) return;
            PlaybackDiagnostics.Write(DiagnosticSector.Google, "Biblioteca", "Sessão expirada ou revogada.");
            _accountExpired = true;
            _accountError = "Sessão Google expirada ou revogada. Entre novamente em Perfil · Minha conta.";
            _music.SetStatus(_accountError);
        }
        catch (Exception error)
        {
            if (!ReferenceEquals(_account, account) || !_music.CanUseOnline) return;
            PlaybackDiagnostics.WriteFailure(DiagnosticSector.Google, "Atualizar biblioteca", error);
            _accountError = "Não foi possível atualizar conta. Tente conexão novamente em Perfil · Minha conta.";
            _music.SetStatus(_accountError);
        }
        finally { _accountNetworkRequest = null; _accountLoading = false; Refresh(); }
    }
    private void DisconnectAccount()
    {
        try { AppServices.ClearYouTubeAccount?.Invoke(); }
        catch (Exception error)
        {
            PlaybackDiagnostics.WriteFailure(DiagnosticSector.Google, "Remover sessão", error);
            throw;
        }
        PlaybackDiagnostics.Write(DiagnosticSector.Google, "Sessão", "Conta desconectada.");
        _accountExpired = false; _accountError = null;
        _account?.Dispose(); _account = null; _accountPlaylists = []; _accountLikes = []; Refresh();
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
    private async Task PlayCollectionAsync(IEnumerable<Track> collection)
    {
        var tracks = (_music.CanUseOnline ? collection : collection.Where(_music.CanPlayOffline)).ToArray();
        if (tracks.Length == 0) return;
        _music.ReplaceQueue(tracks);
        await _music.PlayAsync(tracks[0], 0);
    }
    private static void AddSearchIcon(TextBox field) =>
        field.InnerLeftContent = new Border { Child = Icons.Make("search", Muted), Padding = UiTheme.SearchIconPadding };

    private Control TrackRow(Track track, bool showMenu = false, Func<Task>? playTrack = null, Action<Control>? openMenu = null,
        string? subtitle = null)
    {
        if (track.IsArtist)
        {
            playTrack = () => ShowChannelAsync(track);
            showMenu = false;
            openMenu = null;
            subtitle = "Artista · abrir canal";
        }
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
    private void ShowPlayer() => OpenPlayer(fromSwipe: false);

    private void OpenPlayer(bool fromSwipe)
    {
        if (_showingPlayer) return;
        var track = _music.Pending ?? _music.Current; if (track is null) return;
        if (_overlay is not null) CloseSheet();
        if (_slidingOutPlayer is { } closing)
        {
            _root.Children.Remove(closing);
            _slidingOutPlayer = null;
        }
        _showingPlayer = true; _seeking = false;
        if (_playerPage is not null) _root.Children.Remove(_playerPage);
        var panel = new Grid
        {
            RowDefinitions = new("Auto,*,Auto,Auto,Auto,Auto,Auto"),
            RowSpacing = UiTheme.CompactGap,
            Margin = UiTheme.MobilePlayerMargin,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var header = new Grid { ColumnDefinitions = new("Auto,*,Auto,Auto") };
        header.Children.Add(IconButton("down", "Recolher player", () => { ClosePlayer(); return Task.CompletedTask; }));
        var album = Text(track.Album ?? TrackTextFormatter.Artist(track.Artist), UiTheme.Caption, Muted); album.TextTrimming = TextTrimming.CharacterEllipsis; album.HorizontalAlignment = HorizontalAlignment.Center; Put(header, album, 0, 1);
        Put(header, IconButton("queue", "Abrir fila", () => { OpenQueueFromPlayer(); return Task.CompletedTask; }), 0, 2); panel.Children.Add(header);
        var playerCover = new SquareArtworkSlot();
        Put(panel, playerCover, 1, 0);
        var metadata = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = UiTheme.CompactGap };
        var labels = new StackPanel { Spacing = UiTheme.CompactGap };
        var title = Text(TrackTextFormatter.Title(track.Title, track.Artist), UiTheme.DialogTitle); title.FontWeight = FontWeight.SemiBold; title.TextWrapping = TextWrapping.Wrap; title.MaxLines = 2; labels.Children.Add(title);
        var artist = Text(TrackTextFormatter.Artist(track.Artist), UiTheme.Body, Muted);
        artist.TextTrimming = TextTrimming.CharacterEllipsis;
        var artistLink = ActionButton("", () => OpenTrackArtistAsync(_music.Pending ?? _music.Current ?? track));
        artistLink.Content = artist;
        artistLink.Classes.Add("subtle");
        artistLink.Padding = UiTheme.Zero;
        artistLink.HorizontalContentAlignment = HorizontalAlignment.Left;
        labels.Children.Add(artistLink);
        _expandedRecoveryNotice = Text("", UiTheme.Caption, UiTheme.Warning);
        _expandedRecoveryNotice.IsVisible = false;
        _expandedRecoveryNotice.TextWrapping = TextWrapping.Wrap;
        _expandedRecoveryNotice.MaxLines = 2;
        labels.Children.Add(_expandedRecoveryNotice);
        metadata.Children.Add(labels);
        Track? artworkTrack = null;
        _updatePlayerTrack = UpdatePlayerTrack;
        UpdatePlayerTrack(track);

        void UpdatePlayerTrack(Track displayed)
        {
            album.Text = displayed.Album ?? TrackTextFormatter.Artist(displayed.Artist);
            title.Text = TrackTextFormatter.Title(displayed.Title, displayed.Artist);
            artist.Text = TrackTextFormatter.Artist(displayed.Artist);
            artistLink.IsVisible = !string.IsNullOrEmpty(artist.Text);
            Avalonia.Automation.AutomationProperties.SetName(artistLink, "Ver artista · " + artist.Text);
            var cover = displayed == _music.Current && _music.CurrentSource?.Artwork is { } resolved
                ? displayed with { Thumbnail = resolved } : displayed;
            if (Equals(artworkTrack, cover)) return;
            artworkTrack = cover;
            playerCover.Child = new Artwork(cover, UiTheme.ThumbnailSize, fullResolution: true)
            {
                Width = double.NaN, Height = double.NaN, CornerRadius = new(UiTheme.CardRadius)
            };
        }

        var favorite = _detailFavorite = IconButton("heart", "Alternar favorito", TogglePlaybackFavoriteAsync);
        Put(metadata, favorite, 0, 1); Put(panel, metadata, 2, 0);
        var seek = new Grid { RowDefinitions = new("Auto,Auto") };
        _detailSeek = new Slider { Minimum = 0, Maximum = 1, Margin = UiTheme.Zero, MinHeight = UiTheme.TouchSize,
            Foreground = UiTheme.Brush(Accent), Background = UiTheme.Brush(UiTheme.Outline) };
        _detailSeek.AddHandler(PointerPressedEvent, (_, _) => _seeking = true, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _detailSeek.AddHandler(PointerReleasedEvent, (_, _) => { _seeking = false; _music.Player.Seek(_detailSeek.Value); }, Avalonia.Interactivity.RoutingStrategies.Bubble, true);
        _detailSeek.KeyUp += (_, _) => _music.Player.Seek(_detailSeek.Value); seek.Children.Add(_detailSeek);
        _detailSeek.PointerCaptureLost += (_, _) => _seeking = false;
        var times = new Grid { ColumnDefinitions = new("*,Auto") };
        _detailTime = Text("", UiTheme.Caption, Muted); _detailDuration = Text("", UiTheme.Caption, Muted);
        times.Children.Add(_detailTime); Put(times, _detailDuration, 0, 1); Put(seek, times, 1, 0); Put(panel, seek, 3, 0);
        var controls = new Grid { ColumnDefinitions = new("*,*,Auto,*,*"), ColumnSpacing = UiTheme.ProgressRadius };
        var shuffle = _detailShuffle = IconButton("shuffle", "Aleatório", TogglePlaybackShuffleAsync); shuffle.HorizontalAlignment = HorizontalAlignment.Center; controls.Children.Add(shuffle);
        var previous = IconButton("previous", "Anterior", _music.PreviousAsync); previous.Content = Icons.Make("previous", UiTheme.TextPrimary, UiTheme.TransportIconSize); previous.HorizontalAlignment = HorizontalAlignment.Center; Put(controls, previous, 0, 1);
        var playerPlay = _detailPlay = IconButton("play", "Reproduzir ou pausar", _music.ToggleAsync); _detailPlay.Classes.Add("primary"); _detailPlay.Classes.Add("largePlay"); _detailPlay.Width = _detailPlay.Height = UiTheme.PlaySize; playerPlay.CornerRadius = new(UiTheme.PlaySize / 2); Put(controls, _detailPlay, 0, 2);
        var next = IconButton("next", "Próxima", _music.NextAsync); next.Content = Icons.Make("next", UiTheme.TextPrimary, UiTheme.TransportIconSize); next.HorizontalAlignment = HorizontalAlignment.Center; Put(controls, next, 0, 3);
        var repeat = _detailRepeat = IconButton("repeat", "Repetir", CyclePlaybackRepeatAsync); repeat.HorizontalAlignment = HorizontalAlignment.Center; Put(controls, repeat, 0, 4); Put(panel, controls, 5, 0);
        foreach (var button in new[] { shuffle, previous, next, repeat }) button.Classes.Add("subtle");
        var upcoming = new StackPanel { Spacing = UiTheme.CompactGap };
        upcoming.Children.Add(UiTheme.Text("Próxima", UiTheme.Label, Muted, FontWeight.Medium));
        var nextTrack = _music.Upcoming.FirstOrDefault();
        var nextTitle = _detailNext = Text(nextTrack is null ? "Nenhuma faixa na sequência" : TrackTextFormatter.Title(nextTrack.Title, nextTrack.Artist), UiTheme.ButtonText);
        nextTitle.TextTrimming = TextTrimming.CharacterEllipsis; upcoming.Children.Add(nextTitle);
        var upcomingBox = ActionButton("", () => { OpenQueueFromPlayer(); return Task.CompletedTask; });
        var preview = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = UiTheme.CompactGap };
        preview.Children.Add(upcoming);
        Put(preview, Icons.Make("forward", Muted, 18), 0, 1);
        upcomingBox.Content = preview;
        upcomingBox.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Avalonia.Automation.AutomationProperties.SetName(upcomingBox, "Ver próximas faixas");
        Put(panel, upcomingBox, 6, 0);
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
        playerRadio.Children.Add(ActionButton("Relacionadas", () => { _relatedTab = true; OpenQueueFromPlayer(); return Task.CompletedTask; }));
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
        volumeSlider.ValueChanged += (_, _) => _music.Volume = volumeSlider.Value;
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
        var compactAudio = IconButton("more", "Áudio e volume", () =>
        {
            ShowAudioInfo();
            return Task.CompletedTask;
        });
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
            var wide = !_androidLayout && host.Bounds.Width >= UiTheme.DesktopWidth;
            if (wideLayout != wide)
            {
                wideLayout = wide;
                panel.Children.Clear(); desktopLayout.Children.Clear(); transport.Children.Clear(); host.Children.Clear();
                _compactPlayer = !wide;
                header.Children.Remove(compactAudio);
                controls.Children.Remove(audioTools);
                if (wide)
                {
                    desktopLayout.VerticalAlignment = VerticalAlignment.Stretch;
                    playerCover.MaxWidth = playerCover.MaxHeight = UiTheme.DesktopArtworkMax;
                    queuePanel.VerticalAlignment = VerticalAlignment.Stretch;
                    title.TextWrapping = TextWrapping.NoWrap; title.MaxLines = 1; title.TextTrimming = TextTrimming.CharacterEllipsis;
                    title.FontSize = UiTheme.Body; artist.FontSize = UiTheme.Caption;
                    labels.Children.Remove(title);
                    title.TextTrimming = TextTrimming.None; title.Width = double.NaN;
                    labels.Children.Insert(0, new ScrollingTitle(title));
                    playerPlay.Width = playerPlay.Height = UiTheme.DesktopPlaySize; playerPlay.CornerRadius = new(UiTheme.DesktopPlaySize / 2);
                    transport.ColumnDefinitions = new("*,Auto,*");
                    controls.ColumnDefinitions = new("Auto,Auto,Auto,Auto,Auto,Auto");
                    Put(controls, audioTools, 0, 5);
                    Put(desktopLayout, header, 0, 0); Grid.SetColumnSpan(header, 2);
                    Put(desktopLayout, playerCover, 1, 0); Put(desktopLayout, queuePanel, 1, 1);
                    Put(transport, metadata, 0, 0); metadata.VerticalAlignment = VerticalAlignment.Center;
                    Put(transport, controls, 0, 1);
                    _expandedQuality = Text("", UiTheme.Caption, Muted);
                    _expandedQuality.TextWrapping = TextWrapping.Wrap;
                    _expandedQuality.TextAlignment = TextAlignment.Center;
                    _expandedQuality.VerticalAlignment = VerticalAlignment.Center;
                    var qualityBadge = AudioInfoBadge(_expandedQuality);
                    qualityBadge.HorizontalAlignment = HorizontalAlignment.Right;
                    Put(transport, qualityBadge, 0, 2);
                    RefreshDock();
                    Put(transport, seek, 1, 0); Grid.SetColumnSpan(seek, 3);
                    Put(host, desktopLayout, 0, 0); Put(host, transportFrame, 1, 0);
                }
                else
                {
                    playerCover.MaxWidth = playerCover.MaxHeight = double.PositiveInfinity;
                    _expandedQuality = Text("", UiTheme.Caption, Muted);
                    _expandedQuality.TextWrapping = TextWrapping.Wrap;
                    _expandedQuality.TextAlignment = TextAlignment.Center;
                    var mobileQuality = AudioInfoBadge(_expandedQuality);
                    mobileQuality.HorizontalAlignment = HorizontalAlignment.Center;
                    mobileQuality.VerticalAlignment = VerticalAlignment.Top;
                    mobileQuality.Padding = UiTheme.ChipPadding;
                    Put(header, compactAudio, 0, 3);
                    controls.ColumnDefinitions = new("*,*,Auto,*,*");
                    if (title.Parent is ScrollingTitle scrolling)
                    {
                        labels.Children.Remove(scrolling); scrolling.Children.Remove(title);
                        title.RenderTransform = null; title.Width = double.NaN; labels.Children.Insert(0, title);
                    }
                    title.TextWrapping = TextWrapping.Wrap; title.MaxLines = 2; title.FontSize = UiTheme.DialogTitle; artist.FontSize = UiTheme.Body;
                    playerPlay.Width = playerPlay.Height = UiTheme.PlaySize; playerPlay.CornerRadius = new(UiTheme.PlaySize / 2);
                    Grid.SetColumnSpan(header, 1); Grid.SetColumnSpan(seek, 1);
                    Put(panel, header, 0, 0); Put(panel, playerCover, 1, 0); Put(panel, metadata, 2, 0);
                    Put(panel, seek, 3, 0); Put(panel, controls, 5, 0); Put(panel, upcomingBox, 6, 0);
                    Put(panel, mobileQuality, 4, 0);
                    RefreshDock();
                    host.Children.Add(panel);
                }
            }
            upcomingBox.IsVisible = host.Bounds.Height >= 510;
        }
        // Detach initial mobile composition before responsive host takes ownership.
        panel.Children.Clear();
        host.SizeChanged += (_, _) => { if (_playerPage?.Child == host) ArrangePlayer(); };
        transport.LayoutUpdated += (_, _) => { if (_playerPage?.Child == host) ArrangeTransport(); };
        _playerPage = new Border { Background = UiTheme.Brush(UiTheme.Background), Child = host, ClipToBounds = true };
        Grid.SetColumn(_playerPage, 1); Grid.SetColumnSpan(_playerPage, 2); Grid.SetRowSpan(_playerPage, 3); _root.Children.Add(_playerPage);
        if (_androidLayout)
        {
            SetPlayerSlide(_playerPage, PlayerCollapsedOffset(), animate: false);
            var opening = _playerPage;
            if (!fromSwipe) Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (ReferenceEquals(_playerPage, opening) && !_playerDragging) SetPlayerSlide(opening, 0, animate: true);
            }, Avalonia.Threading.DispatcherPriority.Render);
        }
        else
        {
            _main.IsVisible = false; _mini.IsVisible = false; _desktopDock.IsVisible = false; _queuePane.IsVisible = false; _bottom.IsVisible = false;
            Animate(_playerPage, 14);
        }
        RefreshPlayerQueue(); RefreshPlayback();
    }
    private void ClosePlayer()
    {
        if (!_showingPlayer && _playerPage is null) return;
        if (_playerPage is { } closingPlayer)
        {
            if (_androidLayout) SlidePlayerOut(closingPlayer);
            else RemoveAfterExit(closingPlayer, 14);
        }
        _expandedRecoveryNotice = null;
        _playerPage = null; _showingPlayer = false; _main.IsVisible = true; _bottom.IsVisible = _androidLayout || Bounds.Width < UiTheme.DesktopWidth;
        _detailShuffle = _detailRepeat = _detailFavorite = null; _detailVolume = null;
        _updatePlayerTrack = null;
        _detailQueue = null; _detailNext = _detailQueueHeading = null; _detailRadioToggle = _detailRadioRetry = null;
        _detailSeek = null; _detailPlay = null; _detailTime = null; _detailDuration = null; _seeking = false; Refresh();
    }
    private bool HandleBack()
    {
        if (_googleWelcome is not null) { if (!_googleWelcomeBusy) DismissGoogleWelcome(); return true; }
        if (_topMenuOpen) { CloseTopMenu(); return true; }
        if (_songPopup is { IsOpen: true }) { _songPopup.IsOpen = false; return true; }
        if (_overlay is not null) { CloseSheet(); return true; }
        if (_showingPlayer) { ClosePlayer(); return true; }
        if (_page is "settings" or "account") { BackFromPreferences(); return true; }
        if (_page == "library" && _mobileLibraryGroup is not null) { CloseMobileLibraryGroup(); return true; }
        if (_page == "queue" && _queueOpenedFromPlayer) { ReturnFromMobileQueue(); return true; }
        if (_page == "collection") { BackFromCollection(); return true; }
        if (_page is "queue" or "downloads") { _queueOpen = false; _page = "home"; Refresh(); return true; }
        if (_queueOpen) { _queueOpen = false; Refresh(); return true; }
        return false;
    }
    private static void Animate(Control control, double offset = 0)
    {
        UiMotion.Enter(control, offset);
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
        UpdateRows(_music.Pending ?? _music.Current);
        RefreshTransportButtons();
        var recovery = _music.PlaybackRecoveryMessage;
        _miniRecoveryNotice.Text = recovery;
        _miniRecoveryNotice.IsVisible = recovery is not null;
        if (_expandedRecoveryNotice is { } notice)
        {
            notice.Text = recovery;
            notice.IsVisible = recovery is not null;
        }
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
    private void OnPlaybackStateChanged(object? sender, EventArgs args)
    {
        // Run after Media3's callback so no UI work can interrupt its state update.
        Dispatcher.UIThread.Post(() =>
        {
            if (this.IsAttachedToVisualTree()) RefreshTransportButtons();
        }, DispatcherPriority.Input);
    }

    private void RefreshTransportButtons()
    {
        RefreshPlayButton(_play);
        if (_detailPlay is not null) RefreshPlayButton(_detailPlay);
        if ((_page == "queue" || _queuePane.IsVisible) && _mobileQueuePlay is not null) RefreshPlayButton(_mobileQueuePlay);
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
    private static Control LoadingRing(double size, string color, bool? animate = null)
    {
            var ring = new Avalonia.Controls.Shapes.Ellipse
            {
                Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center, Stroke = UiTheme.Brush(color), StrokeThickness = 2,
                StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { size * .9, size * .8 },
                RenderTransform = new RotateTransform(), RenderTransformOrigin = RelativePoint.Center
            };
            var animation = new Animation { Duration = TimeSpan.FromMilliseconds(800), IterationCount = IterationCount.Infinite };
            animation.Children.Add(new KeyFrame { Cue = new Cue(0), Setters = { new Avalonia.Styling.Setter(RotateTransform.AngleProperty, 0d) } });
            animation.Children.Add(new KeyFrame { Cue = new Cue(1), Setters = { new Avalonia.Styling.Setter(RotateTransform.AngleProperty, 360d) } });
            if (animate ?? !UiMotion.Reduced) ring.Styles.Add(new Avalonia.Styling.Style(s => s.OfType<Avalonia.Controls.Shapes.Ellipse>()) { Animations = { animation } });
            return ring;
    }
    private void ShowSettings()
    {
        ClosePlayer(); CloseTopMenu(); CloseSheet();
        var sections = new List<Control>();
        var sound = new StackPanel { Spacing = UiTheme.CompactGap };
        var volume = new Slider { Minimum = 0, Maximum = 1, Value = _music.Volume, HorizontalAlignment = HorizontalAlignment.Stretch };
        var volumeLabel = Text($"Volume · {_music.Volume:P0}", UiTheme.Body);
        Avalonia.Automation.AutomationProperties.SetName(volume, "Volume nas configurações");
        volume.ValueChanged += (_, _) => { _music.Volume = volume.Value; volumeLabel.Text = $"Volume · {_music.Volume:P0}"; };
        sound.Children.Add(volumeLabel); sound.Children.Add(volume);
        if (AppServices.AudioEngines is { } engines)
        {
            sound.Children.Add(Text("Motor de áudio · requer reinício", UiTheme.Caption, Muted));
            var enginePicker = new ComboBox { ItemsSource = engines.Where(e => AppServices.AudioEngineAvailable?.Invoke(e) == true).ToArray(), SelectedItem = AppServices.SelectedAudioEngine, HorizontalAlignment = HorizontalAlignment.Stretch };
            Avalonia.Automation.AutomationProperties.SetName(enginePicker, "Motor de áudio");
            enginePicker.SelectionChanged += (_, _) => { if (enginePicker.SelectedItem is string engine) { try { AppServices.SaveAudioEngine?.Invoke(engine); } catch { _music.SetStatus("Não foi possível salvar motor de áudio."); } } };
            sound.Children.Add(enginePicker);

        }
        sections.Add(PreferenceSection("Reprodução", sound));
        var reduceMotion = PreferenceCheckBox("Reduzir movimento", _music.ReduceMotion);
        reduceMotion.IsCheckedChanged += (_, _) => _music.SetReduceMotion(reduceMotion.IsChecked == true);
        sections.Add(PreferenceSection("Aparência", reduceMotion));
        sections.Add(PreferenceSection("Conta e biblioteca",
            PreferenceLink("account", "Minha conta", _account?.DisplayName ?? "Google e YouTube Music", ShowAccountAsync),
            PreferenceLink("heart", "Personalizar meu som", "Artistas e estilos favoritos", () => { ShowMusicOnboarding(edit: true); return Task.CompletedTask; }),
            PreferenceLink("queue", "Histórico de escuta", "Reproduções salvas neste dispositivo", () =>
        {
            var history = CollectionList<Track>(track =>
            {
                var row = new StackPanel { Spacing = 4 };
                row.Children.Add(TrackRow(track));
                if (_music.Listening.TryGetValue(MusicController.SongKey(track), out var stat))
                    row.Children.Add(Text($"{stat.Plays} inícios · {Time(stat.Seconds)} ouvidos · {stat.Completed} conclusões · {stat.Skips} pulos rápidos", UiTheme.Caption, Muted));
                return row;
            });
            history.ItemsSource = _music.History.ToArray();
            ShowSheet("Histórico local", history); return Task.CompletedTask;
        })));
        sections.Add(PreferenceSection("Integrações", PreferenceLink("info", "Discord", "Atividade de reprodução", ShowDiscordAccountAsync)));
        var cookies = System.IO.Path.Combine(_music.DataDirectory, "youtube-cookies.txt");
        var support = new List<Control> { PreferenceLink("info", "Diagnósticos", "Relatórios por setor para copiar", () => { ShowDiagnostics(); return Task.CompletedTask; }), PreferenceLink("info", "Privacidade", "Como Hibike trata seus dados", () => OpenLegalAsync("privacy.html")), PreferenceLink("info", "Termos", "Condições de uso", () => OpenLegalAsync("terms.html")) };
        if (System.IO.File.Exists(cookies)) support.Add(PreferenceLink("close", "Apagar cookies antigos (não utilizados)", "Remove arquivo legado", () => { System.IO.File.Delete(cookies); ShowSettings(); return Task.CompletedTask; }));
        if (DeveloperFeatures.Enabled)
        {
            support.Add(PreferenceLink("settings", "Laboratório experimental", "Recursos de desenvolvimento", () => { ShowExperimental(); return Task.CompletedTask; }));
        }
        sections.Add(PreferenceSection("Ajuda e privacidade", support.ToArray()));
        OpenPreferencesPage("settings", sections.ToArray());
    }
    private YouTubeAccountLibrary? _account;

    private Control DiscordTitleNamePreference()
    {
        var option = PreferenceCheckBox("Exibir título da música no lugar de Hibike", _music.DiscordUseTrackTitleAsName);
        option.IsCheckedChanged += (_, _) => _music.SetDiscordUseTrackTitleAsName(option.IsChecked == true);
        return option;
    }

    private Task ShowDiscordAccountAsync()
    {
        if (!OperatingSystem.IsAndroid())
        {
            var desktopPanel = new StackPanel { Spacing = 12 };
            var explanation = Text("O desktop usa o Rich Presence do Discord instalado, sem token pessoal. O Discord precisa estar aberto para mostrar a atividade.", UiTheme.Caption, Muted);
            explanation.TextWrapping = TextWrapping.Wrap;
            desktopPanel.Children.Add(PreferenceSection("Exibição da atividade", DiscordTitleNamePreference(), explanation));
            desktopPanel.Children.Add(PreferenceLink("info", "Diagnóstico do Discord", "", () =>
            {
                ShowDiagnostics(DiagnosticSector.Discord);
                return Task.CompletedTask;
            }));
            ShowSheet("Discord", desktopPanel);
            return Task.CompletedTask;
        }

        var storage = AppServices.DiscordTokenStorage;
        var hasSaved = false;
        var panel = new StackPanel { Spacing = 12 };
        var identity = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = UiTheme.PageGap };
        var avatar = new ContentControl { Content = Icons.Make("account", Muted, 40), Width = 44, Height = 44, VerticalAlignment = VerticalAlignment.Center };
        identity.Children.Add(avatar);
        var identityText = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var profileName = CollectionText("Discord", UiTheme.Body, weight: FontWeight.SemiBold);
        var username = CollectionText("", UiTheme.Caption, Muted);
        var status = Text("", UiTheme.Caption, Muted);
        status.TextWrapping = TextWrapping.Wrap;
        identityText.Children.Add(profileName);
        identityText.Children.Add(username);
        identityText.Children.Add(status);
        Put(identity, identityText, 0, 1);
        var activityLabel = Text("Atividade enviada", UiTheme.Caption, Muted);
        var activity = Text("Sem faixa publicada", UiTheme.Body);
        activity.TextWrapping = TextWrapping.Wrap;
        var activityDetails = new StackPanel { Spacing = 3, Margin = new Thickness(0, 6, 0, 0),
            Children = { activityLabel, activity } };
        var profileLink = PreferenceLink("account", "Abrir perfil no Discord", "", async () =>
        {
            if (_discordAccount?.Profile is { } profile && TopLevel.GetTopLevel(this)?.Launcher is { } launcher)
                await launcher.LaunchUriAsync(new Uri("https://discord.com/users/" + profile.Id));
        });
        panel.Children.Add(PreferenceSection("Perfil e atividade", identity, activityDetails, profileLink));
        panel.Children.Add(PreferenceSection("Exibição da atividade", DiscordTitleNamePreference()));

        var note = Text("Integração não oficial. O Discord pode restringir contas que usam token pessoal.", UiTheme.Caption, Muted);
        note.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(note);
        var token = UiTheme.Field("Token pessoal do Discord");
        token.PasswordChar = '●';
        panel.Children.Add(token);
        var remember = PreferenceCheckBox("Lembrar neste aparelho");
        remember.IsVisible = storage is not null;
        panel.Children.Add(remember);
        var actions = new Grid { ColumnDefinitions = new("*,*"), RowDefinitions = new("Auto,Auto"),
            ColumnSpacing = UiTheme.CompactGap, RowSpacing = UiTheme.CompactGap };
        panel.Children.Add(actions);
        var connect = UiTheme.OutlinedButton("Conectar");
        connect.Classes.Add("primary");
        var useSaved = UiTheme.OutlinedButton("Usar token salvo");
        useSaved.Content = new TextBlock { Text = "Usar token salvo", FontFamily = UiTheme.Inter, FontSize = UiTheme.ButtonText,
            TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
        Avalonia.Automation.AutomationProperties.SetName(useSaved, "Usar token salvo");
        connect.VerticalAlignment = useSaved.VerticalAlignment = VerticalAlignment.Stretch;
        var disconnect = ActionButton("Desconectar e remover token", () =>
        {
            try
            {
                storage?.Clear();
                _discordRetryAllowed = false;
                _discordRetryTimer.Stop();
                _discordRecoveryStatus = null;
                ReplaceDiscordConnection(null);
                CloseSheet();
            }
            catch (Exception error)
            {
                PlaybackDiagnostics.WriteFailure(DiagnosticSector.Discord, "Remover token local", error);
                status.Text = "Não foi possível remover o token salvo.";
            }
            return Task.CompletedTask;
        });
        DiscordAccountPresence.DiscordProfile? rendered = null;
        void UpdatePresence()
        {
            var profile = _discordAccount?.Profile;
            if (!Equals(profile, rendered))
            {
                rendered = profile;
                avatar.Content = profile?.Avatar is { } url
                    ? new Artwork(new Track(profile.Id, profile.Name, "", "", Thumbnail: url), 44) { CornerRadius = new CornerRadius(22) }
                    : Icons.Make("account", Muted, 40);
            }
            profileName.Text = profile?.Name ?? "Discord";
            username.IsVisible = profile is not null;
            username.Text = profile is null ? "" : "@" + profile.Username;
            hasSaved = storage?.Exists() == true;
            useSaved.IsVisible = hasSaved;
            Grid.SetColumnSpan(connect, hasSaved ? 1 : 2);
            disconnect.IsVisible = hasSaved || _discordAccount is not null;
            status.Text = _discordRecoveryStatus ?? _discordAccount?.Status ?? (hasSaved ? "Token salvo neste aparelho" : "Desconectado");
            profileLink.IsVisible = profile is not null;
            var current = _discordAccount?.PublishedTrack;
            activityLabel.IsVisible = current is not null;
            activity.Text = current is null ? "Sem faixa publicada"
                : TrackTextFormatter.Title(current.Title, current.Artist) + " · " + TrackTextFormatter.Artist(current.Artist);
        }
        panel.AttachedToVisualTree += (_, _) =>
        {
            DiscordConnectionChanged += UpdatePresence;
            UpdatePresence();
        };
        panel.DetachedFromVisualTree += (_, _) => DiscordConnectionChanged -= UpdatePresence;

        BindAction(connect, () => ConnectAsync(saved: false));
        BindAction(useSaved, () => ConnectAsync(saved: true));
        actions.Children.Add(connect);
        Put(actions, useSaved, 0, 1);
        Put(actions, disconnect, 1, 0);
        Grid.SetColumnSpan(disconnect, 2);

        panel.Children.Add(PreferenceLink("info", "Diagnóstico do Discord", "", () =>
        {
            ShowDiagnostics(DiagnosticSector.Discord);
            return Task.CompletedTask;
        }));
        ShowSheet("Discord", panel);
        return Task.CompletedTask;

        async Task ConnectAsync(bool saved)
        {
            if (!actions.IsEnabled) return;
            actions.IsEnabled = token.IsEnabled = remember.IsEnabled = false;
            status.Text = "Conectando…";
            try
            {
                string? value;
                try { value = saved ? storage?.Read() : token.Text?.Trim(); }
                catch (Exception error)
                {
                    PlaybackDiagnostics.WriteFailure(DiagnosticSector.Discord, "Ler token local", error);
                    status.Text = "Não foi possível ler o token salvo.";
                    return;
                }
                token.Text = "";
                if (string.IsNullOrWhiteSpace(value)) { status.Text = "Informe um token para conectar."; return; }
                await ConnectDiscordAsync(value, saved, remember.IsChecked == true);
                UpdatePresence();
            }
            catch
            {
                status.Text = saved ? "Não foi possível conectar com o token salvo. Tente informar um novo token."
                    : "Não foi possível conectar. Confira o token e tente novamente.";
            }
            finally { actions.IsEnabled = token.IsEnabled = remember.IsEnabled = true; }
        }
    }

    private Task ShowAccountAsync()
    {
        if (_page != "account") _accountBackToSettings = _page == "settings";
        var sections = new List<Control>();
        var identity = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = UiTheme.PageGap };
        identity.Children.Add(Icons.Make("account", Muted, 36));
        var name = _account?.DisplayName ?? (_account is null ? "Sem conta conectada" : "Conta Google");
        var labels = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(CollectionText(name, UiTheme.SectionTitle, weight: FontWeight.SemiBold));
        labels.Children.Add(CollectionText(_account is null ? "Playlists e curtidas do YouTube Music"
            : _accountExpired ? "Sessão expirada" : "Conectada ao YouTube Music", UiTheme.Caption, Muted));
        Put(identity, labels, 0, 1);
        var connection = new List<Control> { identity };
        if (_accountError is not null)
        {
            var error = Text(_accountExpired ? "Entre novamente para atualizar sua biblioteca."
                : "Não foi possível atualizar a conexão.", UiTheme.Caption, UiTheme.Error);
            error.TextWrapping = TextWrapping.Wrap;
            connection.Add(error);
        }
        if (_account is null || _accountExpired)
            connection.Add(PreferenceLink("account", _accountExpired ? "Entrar novamente com Google" : "Entrar com Google",
                "", () => { ShowGoogleWelcome(); return Task.CompletedTask; }));
        if (_accountRestoreFailed && _account is null && AppServices.RestoreYouTubeAccount is not null)
            connection.Add(PreferenceLink("refresh", "Tentar conexão novamente", "", async () =>
            {
                _accountRestoreAttempted = false;
                await RestoreAccountAsync();
                if (_page == "account") await ShowAccountAsync();
            }));
        if (_account is { } account)
        {
            connection.Add(PreferenceLink("refresh", "Atualizar biblioteca", "", async () =>
            {
                await LoadAccountHomeAsync();
                if (_page == "account") await ShowAccountAsync();
            }));
            connection.Add(PreferenceLink("close", "Desconectar conta", "", () => { DisconnectAccount(); return ShowAccountAsync(); }));
            var library = new List<Control>
            {
                PreferenceLink("heart", "Vídeos curtidos", TrackCount(_accountLikes.Count), () => ShowAccountTracksAsync(account, null, "Vídeos curtidos"))
            };
            library.AddRange(_accountPlaylists.Select(playlist => PreferenceLink("queue", playlist.Title,
                "", () => ShowAccountTracksAsync(account, playlist.Id, playlist.Title))));
            sections.Add(PreferenceSection("Biblioteca do YouTube Music", library.ToArray()));
        }
        var configPath = System.IO.Path.Combine(_music.DataDirectory, "google-oauth-desktop.json");
        if (System.IO.File.Exists(configPath))
            connection.Add(PreferenceLink("close", "Apagar configuração local", "", () =>
            {
                System.IO.File.Delete(configPath);
                DisconnectAccount();
                return ShowAccountAsync();
            }));
        connection.Add(PreferenceLink("info", "Diagnóstico do Google", "", () =>
        {
            ShowDiagnostics(DiagnosticSector.Google);
            return Task.CompletedTask;
        }));
        sections.Insert(0, PreferenceSection("Google", connection.ToArray()));
        OpenPreferencesPage("account", sections.ToArray());
        return Task.CompletedTask;
    }
    private Task ShowAccountTracksAsync(YouTubeAccountLibrary account, string? id, string title)
    {
        string? continuation = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var playlist = _accountPlaylists.FirstOrDefault(item => item.Id == id);
        var cover = playlist?.Thumbnail is { } image ? new Track(playlist.Id, title, "YouTube Music", "", Thumbnail: image) : null;
        return ShowPagedCollectionAsync(title, cover, id is null ? "Curtidas" : "Playlist", async (_, token) =>
        {
            // The provider may return a page made entirely of unavailable tracks.
            var next = continuation;
            var requested = new HashSet<string>(StringComparer.Ordinal);
            while (true)
            {
                var result = await account.TrackPageAsync(id, next, token);
                next = result.Continuation;
                if (next is not null && (seen.Contains(next) || !requested.Add(next)))
                    throw new InvalidOperationException("Continuação repetida na biblioteca.");
                if (result.Tracks.Count == 0 && next is not null) continue;
                return (result.Tracks, next is not null, () =>
                {
                    seen.UnionWith(requested);
                    continuation = next;
                });
            }
        }, sector: DiagnosticSector.Google);
    }
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
        panel.Children.Add(ActionButton("Cancelar downloads ativos", () => { foreach (var job in _downloadQueue.Jobs.ToArray()) _downloadQueue.Cancel(job); return Task.CompletedTask; }));
        panel.Children.Add(ActionButton("Histórico neste aparelho", () =>
        {
            var history = new StackPanel { Spacing = UiTheme.CompactGap };
            foreach (var track in _music.History.ToArray()) history.Children.Add(ActionButton(track.Artist + " — " + track.Title, async () => { CloseSheet(); await _music.PlayAsync(track); }));
            ShowSheet("Histórico", history); return Task.CompletedTask;
        }));
        ShowSheet("Laboratório experimental", panel);
    }
    private async void ShowAudioInfo()
    {
        var source = _music.CurrentSource;
        var track = _music.Current;
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Text(TrackTextFormatter.Title(source?.Title ?? _music.Current?.Title ?? "", source?.Artist ?? _music.Current?.Artist), UiTheme.SectionTitle));
        var adjustableVolume = _compactPlayer && _showingPlayer;
        if (adjustableVolume)
        {
            var volume = new Slider
            {
                Minimum = 0, Maximum = 1, Value = _music.Player.Volume,
                MinHeight = UiTheme.TouchSize, SmallChange = .05, LargeChange = .1
            };
            Avalonia.Automation.AutomationProperties.SetName(volume, "Volume");
            var label = Text($"Volume · {_music.Player.Volume:P0}", UiTheme.Body, Muted);
            volume.ValueChanged += (_, _) =>
            {
                _music.Volume = volume.Value;
                label.Text = $"Volume · {volume.Value:P0}";
            };
            panel.Children.Add(label);
            panel.Children.Add(volume);
        }
        void Detail(string label, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var row = new Grid { ColumnDefinitions = new("*,*"), ColumnSpacing = 12, Margin = new Thickness(0, 4) };
            var caption = Text(label, UiTheme.Caption, Muted); caption.TextWrapping = TextWrapping.Wrap; row.Children.Add(caption);
            Put(row, new SelectableTextBlock { Text = value, FontSize = UiTheme.Body, Foreground = UiTheme.Brush(UiTheme.TextPrimary), TextWrapping = TextWrapping.Wrap }, 0, 1);
            panel.Children.Add(row);
        }
        Detail(_music.Current?.IsLocal == true ? "Bitrate médio do áudio" : "Bitrate da fonte", source?.BitrateKbps is > 0 ? $"{source.BitrateKbps:0.#} kb/s" : null);
        Detail("Artista", string.IsNullOrWhiteSpace(source?.Artist) ? track?.Artist : source.Artist);
        Detail("Origem", track?.IsLocal == true ? "Arquivo local" : "Online");
        if (_music.Player.Duration > 0 || track?.DurationSeconds > 0) Detail("Duração", Time(Math.Max(_music.Player.Duration, track?.DurationSeconds ?? 0)));
        if (_music.Current?.Album is { } album) Detail("Álbum", album);
        if (_music.Current?.TrackNumber is { } number) Detail("Faixa", number);
        if (_music.Current?.Date is { } date) Detail("Data", date);
        Detail("Codec", source?.Codec);
        Detail("Contêiner", source?.Container);
        Detail("Amostragem", source?.SampleRateHz is > 0 ? $"{source.SampleRateHz / 1000:0.#} kHz" : null);
        Detail("Canais", source?.Channels?.ToString("0"));
        if (source?.BitDepth is > 0) Detail("Profundidade", $"{source.BitDepth} bits");
        if (_music.Current?.IsLocal != true) Detail("Formato", source?.Format);
        if (!adjustableVolume) Detail("Volume", $"{_music.Player.Volume:P0}");
        var note = Text(_music.Current?.IsLocal == true
            ? "Dados lidos do arquivo. Em FLAC, bitrate médio calculado pelo tamanho do áudio e número de amostras, sem incluir metadados."
            : "Dados fornecidos pela fonte; bitrate pode ser variável. Valores ausentes não são estimados.", UiTheme.Caption, Muted);
        note.TextWrapping = TextWrapping.Wrap; panel.Children.Add(note);
        if (DeveloperFeatures.Enabled) panel.Children.Add(ActionButton("Abrir diagnóstico", () => { ShowDiagnostics(); return Task.CompletedTask; }));
        var mediaStatus = Text("", UiTheme.Caption, Muted); mediaStatus.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(mediaStatus);
        ShowSheet("Informações do áudio", panel, preservePlayer: true);
        var overlay = _overlay;
        if (track?.IsLocal == true && (!OperatingSystem.IsAndroid() || source?.Codec is null) && AppServices.ReadMediaInfo is { } read)
        {
            mediaStatus.Text = OperatingSystem.IsAndroid() ? "Lendo metadados do arquivo…" : "Lendo MediaInfo…";
            try
            {
                var details = await read(track.Source);
                if (_overlay != overlay || _music.Current != track) return;
                mediaStatus.Text = details.Count == 0 ? "Leitor indisponível ou arquivo não reconhecido. Metadados existentes preservados." : OperatingSystem.IsAndroid() ? "Detalhes adicionais · Android" : "Detalhes adicionais · MediaInfo";
                foreach (var pair in details) Detail(pair.Key, pair.Value);
            }
            catch (Exception) { if (_overlay == overlay) mediaStatus.Text = "Leitura adicional indisponível. Metadados existentes preservados."; }
        }
        else mediaStatus.Text = track?.IsLocal == true ? "MediaInfo não disponível nesta plataforma." : "Dados do fluxo selecionado e do catálogo. MediaInfo não recebe URLs autenticadas nem cookies.";
    }
    private Border AudioInfoBadge(TextBlock label)
    {
        var button = ActionButton("", () => { ShowAudioInfo(); return Task.CompletedTask; });
        button.Classes.Add("subtle"); button.Content = label; button.Padding = UiTheme.Zero;
        button.MinHeight = UiTheme.TouchSize;
        Avalonia.Automation.AutomationProperties.SetName(button, "Informações do áudio");
        ToolTip.SetTip(button, "Informações do áudio");
        var badge = UiTheme.AudioBadge(button);
        badge.Padding = new Thickness(12, 0);
        return badge;
    }
    private void ShowSheet(string title, Control content, bool preservePlayer = false, bool scrollContent = true)
    {
        if (!preservePlayer) ClosePlayer();
        CloseTopMenu();
        if (_overlay is not null) _root.Children.Remove(_overlay);
        var layout = new Grid { RowDefinitions = new("Auto,*,Auto"), RowSpacing = UiTheme.CardGap };
        var label = UiTheme.Text(title, UiTheme.DialogTitle, weight: FontWeight.SemiBold); label.TextWrapping = TextWrapping.Wrap; layout.Children.Add(label);
        Put(layout, scrollContent ? UiTheme.FadingScroll(content) : content, 1, 0);
        Put(layout, ActionButton("Fechar", () => { CloseSheet(); return Task.CompletedTask; }), 2, 0);
        var sheet = UiTheme.Card(layout); sheet.MaxWidth = UiTheme.DialogMaxWidth; sheet.Margin = UiTheme.DialogMargin;
        sheet.HorizontalAlignment = HorizontalAlignment.Stretch; sheet.VerticalAlignment = VerticalAlignment.Center;
        sheet.MaxHeight = Math.Max(UiTheme.TouchSize, Bounds.Height - UiTheme.DialogMargin.Top - UiTheme.DialogMargin.Bottom);
        _overlay = new Border { Background = UiTheme.Brush(UiTheme.Scrim), Child = sheet };
        KeyboardNavigation.SetTabNavigation(_overlay, KeyboardNavigationMode.Cycle);
        _overlay.PointerPressed += (_, e) => { if (ReferenceEquals(e.Source, _overlay)) { CloseSheet(); e.Handled = true; } };
        Grid.SetColumnSpan(_overlay, _root.ColumnDefinitions.Count); Grid.SetRowSpan(_overlay, 3); _root.Children.Add(_overlay); Animate(sheet, _androidLayout ? 12 : 6);
    }
    private void CloseSheet()
    {
        if (_overlay is { } closingSheet) RemoveAfterExit(closingSheet, _androidLayout ? 12 : 6);
        _overlay = null;
        if (!_showingPlayer) { _detailSeek = null; _detailPlay = null; _detailTime = null; _detailDuration = null; _seeking = false; }
    }
    private async void RemoveAfterExit(Control control, double offset)
    {
        control.IsHitTestVisible = false;
        control.IsEnabled = false;
        await UiMotion.ExitAsync(control, offset);
        _root.Children.Remove(control);
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
        button.Click += async (_, _) =>
        {
            try { await action(); }
            catch (Exception ex)
            {
                PlaybackDiagnostics.WriteFailure(DiagnosticSector.App, "Interface", ex);
                _music.SetStatus(ex.Message);
                if (DeveloperFeatures.Enabled) ShowDiagnostics(DiagnosticSector.App);
            }
        };
    }
    private Button IconButton(string icon, string label, Func<Task> action, string color = Accent)
    {
        var button = UiTheme.RoundIconButton(icon, label, color); button.VerticalAlignment = VerticalAlignment.Center;
        BindAction(button, action); return button;
    }
    private static void Put(Grid grid, Control control, int row, int column) { Grid.SetRow(control, row); Grid.SetColumn(control, column); grid.Children.Add(control); }
}
