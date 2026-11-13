using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Hibike.Core.Models;
using Hibike.Core.Services;

namespace Hibike.App.Views;

public sealed partial class MainView
{
    private string? _mobileSearchAttempt;
    private Button _mobileSearchOnline = null!, _mobileSearchLocal = null!, _mobileSearchOptions = null!, _mobileSearchSubmit = null!;
    private Grid _mobileSearchHeading = null!;
    private TextBlock _mobileSearchCount = null!, _mobileSearchError = null!;
    private ContentControl _mobileSearchState = null!;
    private ItemsControl _mobileSearchItems = null!;
    private ScrollViewer _mobileSearchScroll = null!;
    private Track[] _mobileSearchTracks = [];
    private Control _mobileSearchSuggestions = null!, _mobileSearchBusy = null!;
    private string _searchKind = "Tudo";
    private readonly List<Button> _searchKindButtons = [];
    private StackPanel _searchContent = null!, _searchArtists = null!;
    private IReadOnlyList<MusicArtist> _foundArtists = [];
    private IReadOnlyList<ArtistCollection> _foundCollections = [];
    private string? _artistResultsQuery;
    private bool _catalogSearchFailed;
    private TextBlock _searchSongsHeading = null!, _searchProgress = null!;
    private CancellationTokenSource? _artistLookup;

    private Grid BuildMobileSearch()
    {
        var page = new Grid { RowDefinitions = new("Auto,Auto,Auto,Auto,*"), RowSpacing = UiTheme.CompactGap };
        var input = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = UiTheme.CompactGap,
            HorizontalAlignment = HorizontalAlignment.Left };
        page.SizeChanged += (_, e) => input.Width = Math.Min(e.NewSize.Width, 720);
        input.Children.Add(_search);
        _mobileSearchSubmit = ActionButton("Buscar", () =>
        {
            if (_searchScope == "Online" && _mobileSearchAttempt == _search.Text?.Trim() && (_music.Searching || _artistLookup is not null))
            {
                CancelExploreSearch();
                return Task.CompletedTask;
            }
            return SearchAsync();
        });
        Put(input, _mobileSearchSubmit, 0, 1);
        page.Children.Add(input);

        var filterBar = new StackPanel { Spacing = UiTheme.CompactGap };
        var scopes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = UiTheme.CompactGap };
        filterBar.Children.Add(scopes);
        _mobileSearchOnline = ScopeButton("Online");
        _mobileSearchLocal = ScopeButton("Sua biblioteca");
        scopes.Children.Add(_mobileSearchOnline);
        scopes.Children.Add(_mobileSearchLocal);
        var types = new StackPanel { Orientation = Orientation.Horizontal, Spacing = UiTheme.ProgressRadius };
        filterBar.Children.Add(UiTheme.FadingScroll(types, horizontal: true));
        foreach (var kind in new[] { "Tudo", "Músicas", "Artistas", "Álbuns", "Playlists" })
        {
            var button = ActionButton(kind, () => { SelectSearchKind(kind); return Task.CompletedTask; });
            button.Padding = new Thickness(8, 0);
            button.Tag = kind; button.Classes.Add("pill"); _searchKindButtons.Add(button); types.Children.Add(button);
        }
        Put(page, filterBar, 1, 0);

        _mobileSearchHeading = new Grid { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = UiTheme.CompactGap };
        _mobileSearchHeading.Children.Add(CollectionText("Resultados", UiTheme.DialogTitle, weight: FontWeight.SemiBold));
        _mobileSearchCount = CollectionText("", UiTheme.Label, Muted);
        var chip = UiTheme.Chip(_mobileSearchCount);
        chip.VerticalAlignment = VerticalAlignment.Center;
        Put(_mobileSearchHeading, chip, 0, 1);
        _mobileSearchOptions = CollectionIcon("more", "Opções dos resultados", () =>
        {
            ShowMobileSearchOptions();
            return Task.CompletedTask;
        });
        Put(_mobileSearchHeading, _mobileSearchOptions, 0, 2);
        Put(page, _mobileSearchHeading, 2, 0);

        _mobileSearchError = CollectionText("", UiTheme.Caption, UiTheme.Error);
        _mobileSearchError.TextWrapping = TextWrapping.Wrap;
        _mobileSearchError.MaxLines = 3;
        Put(page, _mobileSearchError, 3, 0);

        _mobileSearchItems = CollectionList<Track>(track => TrackRow(track, showMenu: true, selectionTracks: () => _mobileSearchTracks));
        _searchArtists = new StackPanel { Spacing = UiTheme.CompactGap };
        _searchSongsHeading = CollectionText("Músicas", UiTheme.SectionTitle, weight: FontWeight.SemiBold);
        _searchProgress = CollectionText("Carregando mais resultados…", UiTheme.Caption, Muted);
        _searchContent = new StackPanel { Spacing = UiTheme.SectionGap,
            Children = { _searchProgress, _searchArtists, _searchSongsHeading, _mobileSearchItems } };
        _mobileSearchScroll = UiTheme.FadingScroll(_searchContent);
        _mobileSearchState = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch };
        Put(page, new Grid { Children = { _mobileSearchScroll, _mobileSearchState } }, 4, 0);

        _mobileSearchSuggestions = BuildSearchSuggestions();
        var loading = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = UiTheme.CompactGap };
        var spinner = LoadingRing(20, UiTheme.Primary);
        spinner.VerticalAlignment = VerticalAlignment.Center;
        loading.Children.Add(spinner);
        Put(loading, Text("Buscando…", UiTheme.Body), 0, 1);
        var cancel = ActionButton("Cancelar", () => { CancelExploreSearch(); return Task.CompletedTask; });
        cancel.Classes.Add("subtle");
        Put(loading, cancel, 0, 2);
        _mobileSearchBusy = UiTheme.Card(loading, UiTheme.CompactGap);
        _mobileSearchBusy.VerticalAlignment = VerticalAlignment.Top;
        var filterVersion = 0;
        _search.TextChanged += async (_, _) =>
        {
            var version = ++filterVersion;
            if (_page != "explore") return;
            if (_mobileSearchAttempt != _search.Text?.Trim()) CancelExploreSearch();
            if (_searchScope == "Sua biblioteca" || !_music.CanUseOnline) await Task.Delay(120);
            if (version == filterVersion && _page == "explore") RefreshMobileSearch();
        };
        MakeTopRowsCollapsible(page, 3);
        return page;

        Button ScopeButton(string name)
        {
            var button = ActionButton(name, () =>
            {
                if (_searchScope == name || name == "Online" && !_music.CanUseOnline) return Task.CompletedTask;
                if (name == "Sua biblioteca") CancelExploreSearch();
                _searchScope = name;
                _mobileSearchScroll.Offset = default;
                RefreshMobileSearch();
                return Task.CompletedTask;
            });
            button.Classes.Add("pill");
            return button;
        }
    }

    private Control BuildSearchSuggestions()
    {
        var panel = new StackPanel { Spacing = UiTheme.SectionGap, Margin = new Thickness(0, UiTheme.CompactGap, 0, 0) };
        panel.Children.Add(CollectionText("Explorar", UiTheme.DialogTitle, weight: FontWeight.SemiBold));
        panel.Children.Add(Text("Escolha um tema para começar.", UiTheme.Caption, Muted));
        var suggestions = new WrapPanel { ItemSpacing = UiTheme.CompactGap, LineSpacing = UiTheme.CompactGap };
        foreach (var (label, query) in new[] { ("Gêneros", "jazz soul instrumental music"), ("Foco", "instrumental focus music"),
            ("Energia", "energetic music"), ("Trilhas sonoras", "original soundtrack music") })
        {
            var button = ActionButton(label, async () => { _search.Text = query; await SearchAsync(); });
            button.Classes.Add("pill");
            suggestions.Children.Add(button);
        }
        panel.Children.Add(suggestions);
        return panel;
    }

    private void SelectSearchKind(string kind)
    {
        if (_searchKind == kind) return;
        _searchKind = kind;
        _mobileSearchScroll.Offset = default;
        RefreshMobileSearch();
    }

    private void CancelExploreSearch()
    {
        _artistLookup?.Cancel();
        _music.CancelSearch();
    }

    private Control SearchCatalogRow(ArtistCollection entry, Func<Task> open, string? subtitle = null)
    {
        var row = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = UiTheme.PageGap };
        var art = new Artwork(new(entry.Id, entry.Title, "", "", Thumbnail: entry.Image), 48);
        if (entry.Kind == "Artista") art.CornerRadius = new CornerRadius(24);
        row.Children.Add(art);
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        labels.Children.Add(CollectionText(entry.Title, UiTheme.Body, weight: FontWeight.SemiBold));
        labels.Children.Add(CollectionText(subtitle ?? (entry.Artist is { } artist ? entry.Kind + " · " + artist.Name : entry.Kind), UiTheme.Caption, Muted));
        Put(row, labels, 0, 1);
        Put(row, Icons.Make("forward", Muted), 0, 2);
        var button = ActionButton("", open);
        button.Classes.Add("subtle");
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.Content = row;
        Avalonia.Automation.AutomationProperties.SetName(button, entry.Title + " · " + entry.Kind);
        return button;
    }

    private void RefreshMobileSearch()
    {
        var query = _search.Text?.Trim() ?? "";
        var local = _searchScope == "Sua biblioteca" || !_music.CanUseOnline;
        _mobileSearchOnline.IsEnabled = _music.CanUseOnline;
        var busy = !local && _mobileSearchAttempt == query && (_music.Searching || _artistLookup is not null);
        var hasResult = !local && query.Length > 0 && _music.ResultsQuery == query;
        var error = !local && query.Length > 0 && _mobileSearchAttempt == query && (_music.SearchError is not null || _catalogSearchFailed);
        _mobileSearchOnline.Classes.Set("primary", !local);
        _mobileSearchLocal.Classes.Set("primary", local);
        foreach (var button in _searchKindButtons) { button.Classes.Set("primary", Equals(button.Tag, _searchKind)); button.IsVisible = !local; }
        _mobileSearchSubmit.IsEnabled = query.Length > 0;
        _mobileSearchSubmit.Content = busy ? "Cancelar" : "Buscar";
        _mobileSearchSubmit.Classes.Set("primary", _mobileSearchSubmit.IsEnabled);
        _clearSearch.IsVisible = !string.IsNullOrEmpty(_search.Text);

        var tracks = local && query.Length > 0
            ? (_music.CanUseOnline ? _music.Favorites.Concat(_music.OfflineTracks) : _music.OfflineTracks)
                .DistinctBy(track => track.Source).Where(track => MatchesLibraryQuery($"{track.Title} {track.Artist} {track.Album}", query)).ToArray()
            : hasResult && _searchKind is "Tudo" or "Músicas" ? _music.Results.Where(t => !t.IsArtist).ToArray() : [];
        var artists = !local && _searchKind is "Tudo" or "Artistas" && _artistResultsQuery == query ? _foundArtists : [];
        var collections = !local && _artistResultsQuery == query ? _foundCollections.Where(c => _searchKind == "Tudo" || (_searchKind == "Álbuns" && c.Kind != "Playlist") || (_searchKind == "Playlists" && c.Kind == "Playlist")).ToArray() : [];
        _searchArtists.IsVisible = artists.Count > 0 || collections.Length > 0;
        var catalogKey = (query, _searchKind, artists, _foundCollections);
        if (!Equals(_searchArtists.Tag, catalogKey))
        {
            _searchArtists.Tag = catalogKey; _searchArtists.Children.Clear();
            AddCatalog("Artistas", "Artistas", artists.Select(a => new ArtistCollection(a.Id, a.Name, "Artista", a.Image)),
                c => ShowArtistAsync(new(c.Id, c.Title, c.Image)));
            foreach (var group in collections.GroupBy(c => c.Kind == "Playlist").OrderBy(group => group.Key))
                AddCatalog(group.Key ? "Playlists" : "Álbuns e singles", group.Key ? "Playlists" : "Álbuns", group,
                    c => OpenArtistCollectionAsync(c, c.Artist is { } artist
                        ? new(artist.Id, artist.Name, artist.Name, "https://music.youtube.com/channel/" + artist.Id, Thumbnail: c.Image)
                        : new(c.Id, c.Title, "YouTube Music", "", Thumbnail: c.Image)));

            void AddCatalog(string title, string kind, IEnumerable<ArtistCollection> source, Func<ArtistCollection, Task> open)
            {
                var entries = source.ToArray();
                if (entries.Length == 0) return;
                if (_searchKind == "Tudo")
                {
                    _searchArtists.Children.Add(ArtistShelf(title, entries.Take(8), open, () =>
                    {
                        SelectSearchKind(kind);
                        return Task.CompletedTask;
                    }));
                }
                else
                {
                    var list = CollectionList<ArtistCollection>(entry => SearchCatalogRow(entry, () => open(entry)));
                    list.ItemsSource = entries;
                    _searchArtists.Children.Add(list);
                }
            }
        }
        if (!_mobileSearchTracks.SequenceEqual(tracks))
        {
            _mobileSearchTracks = tracks;
            _mobileSearchItems.ItemsSource = tracks;
            _mobileSearchScroll.Offset = default;
        }
        _searchSongsHeading.IsVisible = !local && _searchKind == "Tudo" && tracks.Length > 0 && _searchArtists.IsVisible;
        _searchProgress.IsVisible = busy;
        var notice = error && !busy ? "Parte da busca falhou. Você pode tentar novamente." : null;
        _mobileSearchError.Text = notice ?? "";
        _mobileSearchError.IsVisible = !string.IsNullOrWhiteSpace(notice);
        var any = tracks.Length > 0 || artists.Count > 0 || collections.Length > 0;
        _mobileSearchHeading.IsVisible = any;
        _mobileSearchScroll.IsVisible = any;
        _mobileSearchState.IsVisible = !any;
        _mobileSearchCount.Text = (tracks.Length + artists.Count + collections.Length) + " resultados";
        _mobileSearchOptions.IsVisible = tracks.Length > 0;
        if (any) return;

        var state = busy ? "busy" : query.Length == 0 ? local ? "local" : "explore"
            : error ? "error" : local || hasResult || _artistResultsQuery == query ? "no-results" : _mobileSearchAttempt == query ? "retry" : "ready";
        if (Equals(_mobileSearchState.Tag, state)) return;
        _mobileSearchState.Tag = state;
        _mobileSearchState.Content = state switch
        {
            "busy" => _mobileSearchBusy,
            "explore" => _mobileSearchSuggestions,
            "local" => EmptyState("library", "Busque na sua biblioteca", "Encontre músicas salvas pelo título, artista ou álbum."),
            "error" or "retry" => EmptyState("search", "Busca interrompida", "Tente buscar novamente.", "Tentar novamente", SearchAsync),
            "no-results" => EmptyState("search", "Nenhum resultado", "Tente outro título ou nome de artista."),
            _ => EmptyState("search", "Pronto para buscar", "Toque em Buscar ou use o teclado.")
        };
        if (_mobileSearchPage?.IsEffectivelyVisible == true) Animate(_mobileSearchState, 4);
    }

    private async Task SearchArtistResultsAsync(string query)
    {
        _artistLookup?.Cancel();
        using var request = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        _artistLookup = request;
        _catalogSearchFailed = false;
        if (_artistResultsQuery != query)
        {
            _foundArtists = [];
            _foundCollections = [];
            _artistResultsQuery = null;
        }
        try
        {
            if (!Uri.TryCreate(query, UriKind.Absolute, out _))
                await Task.WhenAll(
                    LoadAsync(token => _music.SearchArtistsAsync(query, token), result => _foundArtists = result),
                    LoadAsync(token => MusicDiscovery.SearchCollectionsAsync(query, playlists: false, token), result => ApplyCollections(result, false)),
                    LoadAsync(token => MusicDiscovery.SearchCollectionsAsync(query, playlists: true, token), result => ApplyCollections(result, true)));
        }
        finally
        {
            if (ReferenceEquals(_artistLookup, request))
            {
                _artistLookup = null;
                if (_page == "explore") RefreshMobileSearch();
            }
        }

        void ApplyCollections(IReadOnlyList<ArtistCollection> result, bool playlists) =>
            _foundCollections = _foundCollections.Where(entry => (entry.Kind == "Playlist") != playlists).Concat(result).ToArray();

        async Task LoadAsync<T>(Func<CancellationToken, Task<IReadOnlyList<T>>> load, Action<IReadOnlyList<T>> apply)
        {
            try
            {
                var result = await load(request.Token);
                if (!ReferenceEquals(_artistLookup, request) || request.IsCancellationRequested) return;
                apply(result);
                _artistResultsQuery = query;
            }
            catch (OperationCanceledException) when (request.IsCancellationRequested) { }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                PlaybackDiagnostics.Write("Busca no catálogo", error.Message);
                if (ReferenceEquals(_artistLookup, request)) _catalogSearchFailed = true;
            }
            if (ReferenceEquals(_artistLookup, request) && _page == "explore") RefreshMobileSearch();
        }
    }

    private void ShowMobileSearchOptions()
    {
        var tracks = _mobileSearchTracks;
        var panel = new StackPanel { Spacing = UiTheme.CompactGap };
        panel.Children.Add(ActionButton("Tocar resultados", async () => { CloseSheet(); await PlayCollectionAsync(tracks); }));
        panel.Children.Add(ActionButton("Adicionar resultados à fila", () =>
        {
            foreach (var track in tracks) _music.Enqueue(track);
            CloseSheet();
            return Task.CompletedTask;
        }));
        ShowSheet("Resultados", panel);
    }
}
