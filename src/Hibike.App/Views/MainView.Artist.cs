using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Hibike.Core.Models;
using Hibike.Core.Services;

namespace Hibike.App.Views;

public sealed partial class MainView
{
    private readonly Dictionary<string, ArtistProfile> _artistProfiles = [];

    private async Task<ArtistProfile> GetArtistProfileAsync(string id, CancellationToken token)
    {
        if (_artistProfiles.TryGetValue(id, out var cached)) return cached;
        var profile = await MusicDiscovery.ArtistAsync(id, token);
        _artistProfiles[id] = profile;
        return profile;
    }

    private async Task OpenTrackArtistAsync(Track track, bool replace = false)
    {
        var name = TrackTextFormatter.Artist(track.Artist);
        if (string.IsNullOrWhiteSpace(name)) return;
        bool Matches(string artist) => System.Globalization.CultureInfo.CurrentCulture.CompareInfo.Compare(
            TrackTextFormatter.Artist(artist), name, System.Globalization.CompareOptions.IgnoreCase |
            System.Globalization.CompareOptions.IgnoreNonSpace) == 0;
        var saved = _music.Favorites.Concat(_music.History).Where(item => Matches(item.Artist))
            .Append(track).Where(item => !item.IsArtist).DistinctBy(item => item.Source).ToArray();
        var artwork = track with { Title = name };
        if (!_music.CanUseOnline)
        {
            OpenCollection(name, _music.OfflineTracks.Where(item => Matches(item.Artist)).ToArray(),
                "Artista · neste aparelho", artwork, replace: replace);
            return;
        }
        OpenCollection(name, [], "Artista", artwork, "Buscando perfil no YouTube Music…", replace: replace, artistPage: true);
        var page = _collectionPage.Child;
        bool IsCurrent() => _page == "collection" && ReferenceEquals(page, _collectionPage.Child);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            var candidates = await _music.SearchArtistsAsync(name, timeout.Token);
            if (!IsCurrent()) return;
            var exact = candidates.Where(artist => Matches(artist.Name)).Take(2).ToArray();
            if (exact.Length == 1)
            {
                await ShowArtistAsync(exact[0], replace: true);
                return;
            }

            OpenCollection(name, saved, "Artista · na sua biblioteca", artwork,
                "Nenhuma faixa salva deste artista.", replace: true, artistPage: true);
            if (candidates.Count == 0) return;
            var grid = (Grid)_collectionPage.Child!;
            var actions = CollectionActions(grid);
            actions.Children.Add(ActionButton("Perfis no YouTube Music", () =>
            {
                var profiles = CollectionList<MusicArtist>(artist => SearchCatalogRow(
                    new(artist.Id, artist.Name, "Artista", artist.Image), () => ShowArtistAsync(artist)));
                profiles.ItemsSource = candidates;
                ShowSheet("Perfis no YouTube Music", profiles);
                return Task.CompletedTask;
            }));
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            PlaybackDiagnostics.Write("Perfil do artista", error.Message);
            if (!IsCurrent()) return;
            OpenCollection(name, saved, "Artista · na sua biblioteca", artwork, "Perfil online indisponível.",
                () => OpenTrackArtistAsync(track, replace: true), replace: true, artistPage: true);
            if (saved.Length > 0 && _collectionPage.Child is Grid grid)
            {
                var actions = CollectionActions(grid);
                actions.Children.Add(ActionButton("Tentar perfil online", () => OpenTrackArtistAsync(track, replace: true)));
            }
        }
    }

    private void ShowArtistCatalog(MusicArtist artist, ArtistProfile profile)
    {
        var artwork = new Track(artist.Id, artist.Name, artist.Name, "https://www.youtube.com/channel/" + artist.Id, Thumbnail: profile.Image ?? artist.Image);
        OpenCollection(artist.Name, profile.Tracks, "Artista", artwork, replace: true, artistPage: true);
        var grid = (Grid)_collectionPage.Child!;
        var body = new StackPanel { Spacing = UiTheme.SectionGap, Margin = new Thickness(0, UiTheme.PageGap) };
        var videos = ActionButton("Ver todos os vídeos", () => ShowPagedCollectionAsync(artist.Name, artwork.Source + "/videos", artwork, "Vídeos"));
        var navigation = new WrapPanel { ItemSpacing = UiTheme.CompactGap, LineSpacing = UiTheme.CompactGap };
        navigation.Children.Add(videos);
        navigation.Children.Add(ActionButton("Playlists do canal", () => ShowChannelPlaylistsAsync(artwork)));
        if (profile.Collections.Count == 0)
            navigation.Children.Add(ActionButton("Buscar discografia no YouTube Music", async () =>
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
                try
                {
                    var matches = await _music.SearchArtistsAsync(artist.Name, timeout.Token);
                    var match = matches.FirstOrDefault(a => a.Id != artist.Id && string.Equals(a.Name.Trim(), artist.Name.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (_page != "collection" || !ReferenceEquals(_collectionPage.Child, grid)) return;
                    if (match is not null) await ShowArtistAsync(match);
                    else _music.SetStatus("Nenhum outro perfil musical com esse nome encontrado.");
                }
                catch (Exception e) when (e is not OutOfMemoryException) { _music.SetStatus("Busca de discografia indisponível: " + e.Message); }
            }));
        if (profile.Tracks.Count > 0)
        {
            body.Children.Add(Text("Músicas em destaque", UiTheme.SectionTitle));
            foreach (var track in profile.Tracks.Take(5)) body.Children.Add(TrackRow(track, showMenu: true));
            if (profile.Tracks.Count > 5) body.Children.Add(ActionButton("Mostrar todas", () => { OpenCollection(artist.Name, profile.Tracks, "Músicas", artwork); return Task.CompletedTask; }));
        }
        foreach (var kind in new[] { "Álbum", "Single / EP", "Playlist" }.Concat(profile.Collections.Select(c => c.Kind)).Distinct())
        {
            var collections = profile.Collections.Where(c => c.Kind == kind).ToArray();
            if (collections.Length == 0) continue;
            var section = profile.Sections.FirstOrDefault(s => kind == "Single / EP" ? s.Title.Contains("Singles") : kind == "Álbum" ? s.Title.Contains("Albums") : s.Title.Contains("Playlists"));
            body.Children.Add(ArtistShelf(kind == "Álbum" ? "Álbuns" : kind == "Single / EP" ? "Singles e EPs" : "Playlists", collections,
                c => OpenArtistCollectionAsync(c, artwork), section is null ? null : () => ShowMusicSectionAsync(artist, section)));
        }
        foreach (var section in profile.Sections.Where(s => s.Title.Contains("parcial", StringComparison.OrdinalIgnoreCase)))
            body.Children.Add(ActionButton(section.Title, () => ShowMusicSectionAsync(artist, section)));
        if (profile.Tracks.Count == 0 && profile.Collections.Count == 0)
            body.Children.Add(Text("O catálogo não retornou faixas nem lançamentos para este perfil.", UiTheme.Caption, Muted));
        body.Children.Add(Text("Mais do artista", UiTheme.SectionTitle));
        body.Children.Add(navigation);
        var row = grid.RowDefinitions.Count - 1;
        var previous = CollectionBody(grid);
        grid.Children.Remove(previous);
        Put(grid, UiTheme.FadingScroll(body), row, 0);
    }

    private Control ArtistShelf(string title, IEnumerable<ArtistCollection> entries, Func<ArtistCollection, Task> open, Func<Task>? more = null)
    {
        var section = new StackPanel { Spacing = UiTheme.CompactGap };
        var header = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = UiTheme.CompactGap };
        header.Children.Add(Text(title, UiTheme.DialogTitle));
        var scroll = UiTheme.FadingScroll(horizontal: true);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        if (more is not null) actions.Children.Add(ActionButton("Mostrar tudo", more));
        if (!_androidLayout)
        {
            actions.Children.Add(IconButton("back", "Voltar em " + title, () => { scroll.Offset = new Vector(Math.Max(0, scroll.Offset.X - scroll.Viewport.Width * .8), 0); return Task.CompletedTask; }));
            actions.Children.Add(IconButton("forward", "Avançar em " + title, () => { scroll.Offset = new Vector(Math.Min(Math.Max(0, scroll.Extent.Width - scroll.Viewport.Width), scroll.Offset.X + scroll.Viewport.Width * .8), 0); return Task.CompletedTask; }));
        }
        Put(header, actions, 0, 1); section.Children.Add(header);
        var cards = CollectionList<ArtistCollection>(c =>
        {
            var card = CoverCard(new(c.Id, c.Title, title, "", Thumbnail: c.Image), c.Artist?.Name ?? c.Kind, () => open(c), songActions: false,
                cardSize: _androidLayout ? MobileHomeCardSize : 196, roundArtwork: c.Kind == "Artista");
            card.Margin = new Thickness(0, 0, UiTheme.PageGap, 0);
            return card;
        });
        cards.ItemsPanel = new Avalonia.Controls.Templates.FuncTemplate<Panel?>(() => new VirtualizingStackPanel { Orientation = Orientation.Horizontal });
        cards.ItemsSource = entries.ToArray(); scroll.Content = cards; section.Children.Add(scroll);
        return section;
    }

    private async Task ShowMusicSectionAsync(MusicArtist artist, ArtistSection section, bool replace = false)
    {
        var artwork = new Track(artist.Id, artist.Name, artist.Name, "https://www.youtube.com/channel/" + artist.Id, Thumbnail: artist.Image);
        if (section.Id.StartsWith("VL", StringComparison.Ordinal))
        {
            await ShowPagedCollectionAsync(section.Title, "https://www.youtube.com/playlist?list=" + Uri.EscapeDataString(section.Id[2..]), artwork, "Catálogo musical", replace);
            return;
        }
        OpenCollection(section.Title, [], "Catálogo musical", artwork, "Buscando catálogo completo…", replace: replace);
        var page = _collectionPage.Child;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var profile = await MusicDiscovery.ArtistSectionAsync(section, timeout.Token);
            if (_page != "collection" || !ReferenceEquals(page, _collectionPage.Child)) return;
            OpenCollection(section.Title, profile.Tracks, "Catálogo musical", artwork, replace: true);
            if (profile.Collections.Count == 0) return;
            var grid = (Grid)_collectionPage.Child!;
            ItemsControl BuildReleases()
            {
                var list = CollectionList<ArtistCollection>(entry => SearchCatalogRow(entry, () => OpenArtistCollectionAsync(entry, artwork)));
                list.ItemsSource = profile.Collections;
                return list;
            }
            if (profile.Tracks.Count > 0)
            {
                var actions = CollectionActions(grid);
                actions.Children.Add(ActionButton("Ver lançamentos", () =>
                {
                    ShowSheet(section.Title, BuildReleases());
                    return Task.CompletedTask;
                }));
            }
            else
            {
                var row = grid.RowDefinitions.Count - 1;
                var previous = CollectionBody(grid);
                grid.Children.Remove(previous);
                Put(grid, UiTheme.FadingScroll(BuildReleases()), row, 0);
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            PlaybackDiagnostics.Write("Discografia", e.Message);
            if (_page == "collection" && ReferenceEquals(page, _collectionPage.Child))
                OpenCollection(section.Title, [], "Catálogo musical", artwork, "Catálogo indisponível", () => ShowMusicSectionAsync(artist, section, replace: true), replace: true);
        }
    }

    private async Task OpenArtistCollectionAsync(ArtistCollection collection, Track artist, bool replace = false)
    {
        var playlist = collection.Id.StartsWith("VL", StringComparison.Ordinal) ? collection.Id[2..] : null;
        if (playlist is null)
        {
            OpenCollection(collection.Title, [], collection.Kind, artist with { Thumbnail = collection.Image ?? artist.Thumbnail }, "Buscando álbum…", replace: replace);
            replace = true;
            var page = _collectionPage.Child;
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
                playlist = await MusicDiscovery.AlbumPlaylistAsync(collection.Id, timeout.Token);
                if (_page != "collection" || !ReferenceEquals(page, _collectionPage.Child)) return;
                if (playlist is null) throw new InvalidOperationException("Álbum sem playlist disponível.");
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                if (_page == "collection" && ReferenceEquals(page, _collectionPage.Child))
                    OpenCollection(collection.Title, [], collection.Kind, artist, "Álbum indisponível", () => OpenArtistCollectionAsync(collection, artist, replace: true), replace: true);
                return;
            }
        }
        await ShowPagedCollectionAsync(collection.Title, "https://www.youtube.com/playlist?list=" + Uri.EscapeDataString(playlist),
            artist with { Thumbnail = collection.Image ?? artist.Thumbnail }, collection.Kind, replace);
    }

    private Task ShowPagedCollectionAsync(string title, string source, Track artwork, string kind, bool replace = false) =>
        ShowPagedCollectionAsync(title, artwork, kind, async (index, token) =>
        {
            var tracks = await _music.BrowsePageAsync(source, index, token);
            return (tracks, tracks.Count >= 50, null);
        }, replace);

    private async Task ShowPagedCollectionAsync(string title, Track? artwork, string kind,
        Func<int, CancellationToken, Task<(IReadOnlyList<Track> Tracks, bool HasMore, Action? Advance)>> load, bool replace = false, DiagnosticSector sector = DiagnosticSector.Catalog)
    {
        var tracks = new List<Track>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pageIndex = 0;
        OpenCollection(title, [], kind, artwork, "Buscando conteúdo…", replace: replace);
        var page = _collectionPage.Child;
        Button? more = null;
        var loading = false;
        await LoadAsync();

        async Task LoadAsync()
        {
            if (loading || _page != "collection" || !ReferenceEquals(page, _collectionPage.Child)) return;
            if (!_music.CanUseOnline)
            {
                if (tracks.Count == 0)
                {
                    OpenCollection(title, [], kind, artwork, "Disponível no modo online", LoadAsync, replace: true);
                    page = _collectionPage.Child;
                }
                return;
            }
            loading = true;
            var trigger = more;
            if (trigger is not null)
            {
                trigger.Content = "Carregando…";
                trigger.IsEnabled = false;
            }
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                var (batch, hasMore, advance) = await load(pageIndex, timeout.Token);
                if (_page != "collection" || !ReferenceEquals(page, _collectionPage.Child)) return;
                var next = batch.Where(t => !t.IsArtist && seen.Add(t.Source))
                    .Select(t => t.Artist == "YouTube" && artwork is not null ? t with { Artist = artwork.Artist } : t).ToArray();
                tracks.AddRange(next);
                pageIndex++;
                more = hasMore && tracks.Count > 0 ? CollectionAction("Carregar mais", LoadAsync) : null;
                OpenCollection(title, tracks.ToArray(), kind, artwork, replace: true, footer: more);
                page = _collectionPage.Child;
                advance?.Invoke();
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                PlaybackDiagnostics.WriteFailure(sector, "Carregar página da coleção", e);
                if (_page != "collection" || !ReferenceEquals(page, _collectionPage.Child)) return;
                if (tracks.Count == 0)
                {
                    OpenCollection(title, [], kind, artwork, "Conteúdo indisponível", LoadAsync, replace: true);
                    page = _collectionPage.Child;
                }
                else if (trigger is not null) trigger.Content = "Tentar carregar mais";
            }
            finally
            {
                loading = false;
                if (trigger is not null) trigger.IsEnabled = true;
            }
        }
    }

    private async Task ShowChannelPlaylistsAsync(Track artist)
    {
        OpenCollection(artist.Title, [], "Playlists do canal", artist, "Buscando playlists…");
        var page = _collectionPage.Child;
        var index = 0;
        var playlists = new List<Track>();
        var rows = new System.Collections.ObjectModel.ObservableCollection<object>();
        var columns = 1;
        var cardSize = _androidLayout ? MobileHomeCardSize : 196;
        var cards = CollectionList<object>(entry =>
        {
            if (entry is not Track[] items) return new Border();
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = UiTheme.PageGap,
                Margin = new Thickness(0, 0, 0, UiTheme.PageGap) };
            foreach (var item in items)
                row.Children.Add(CoverCard(item, "Playlist", () => ShowPagedCollectionAsync(item.Title,
                    "https://www.youtube.com/playlist?list=" + Uri.EscapeDataString(item.Id),
                    artist with { Thumbnail = item.Thumbnail ?? artist.Thumbnail }, "Playlist"),
                    songActions: false, cardSize: cardSize));
            return row;
        });
        cards.ItemsSource = rows;
        var scroll = UiTheme.FadingScroll(cards);
        var more = ActionButton("Carregar mais playlists", () => Task.CompletedTask);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        more.Click += async (_, _) => await LoadAsync();
        scroll.SizeChanged += (_, e) =>
        {
            var width = e.NewSize.Width;
            if (width <= 0 || width == e.PreviousSize.Width) return;
            var size = Math.Min(_androidLayout ? MobileHomeCardSize : 196, width);
            var count = Math.Max(1, (int)((width + UiTheme.PageGap) / (size + UiTheme.PageGap)));
            if (count == columns && size == cardSize) return;
            cardSize = size;
            columns = count;
            var hasMore = rows.Contains(more);
            rows.Clear();
            AppendRows(0);
            if (hasMore) rows.Add(more);
        };
        void AppendRows(int first)
        {
            foreach (var row in playlists.Skip(first).Chunk(columns)) rows.Add(row);
        }
        await LoadAsync();
        async Task LoadAsync()
        {
            if (!more.IsEnabled) return;
            more.IsEnabled = false;
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                var batch = await _music.BrowsePageAsync(artist.Source + "/playlists", index, timeout.Token);
                if (_page != "collection" || !ReferenceEquals(page, _collectionPage.Child)) return;
                rows.Remove(more);
                var first = playlists.Count / columns * columns;
                if (first < playlists.Count) rows.RemoveAt(rows.Count - 1);
                var next = batch.Where(track => seen.Add(track.Id)).ToArray();
                playlists.AddRange(next);
                AppendRows(first);
                index++;
                more.Content = "Carregar mais playlists";
                if (batch.Count >= 50 && next.Length > 0) rows.Add(more);
                if (index != 1) return;
                var grid = (Grid)page!;
                var row = grid.RowDefinitions.Count - 1;
                grid.Children.Remove(CollectionBody(grid));
                Put(grid, playlists.Count == 0 ? Text("Nenhuma playlist pública neste canal.", UiTheme.Body) : scroll, row, 0);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                PlaybackDiagnostics.Write("Playlists do canal", e.Message);
                if (_page == "collection" && ReferenceEquals(page, _collectionPage.Child))
                {
                    if (playlists.Count == 0)
                    {
                        OpenCollection(artist.Title, [], "Playlists do canal", artist, "Playlists indisponíveis", LoadAsync, replace: true);
                        page = _collectionPage.Child;
                    }
                    else more.Content = "Falha ao carregar · tentar novamente";
                }
            }
            finally { more.IsEnabled = true; }
        }
    }
}
