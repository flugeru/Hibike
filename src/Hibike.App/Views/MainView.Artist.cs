using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
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
            var highlights = profile.Tracks.Take(5).ToArray();
            foreach (var track in highlights)
                body.Children.Add(TrackRow(track, showMenu: true, selectionTracks: () => highlights));
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
        if (_androidLayout) scroll.Margin = new Thickness(-UiTheme.MobileGutter, 0);
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
            card.Margin = new Thickness(0, 0, _androidLayout ? UiTheme.CompactGap : UiTheme.PageGap, 0);
            return card;
        });
        cards.ItemsPanel = new Avalonia.Controls.Templates.FuncTemplate<Panel?>(() => new VirtualizingStackPanel { Orientation = Orientation.Horizontal });
        if (_androidLayout) cards.Margin = new Thickness(UiTheme.MobileGutter, 0, UiTheme.MobileGutter - UiTheme.CompactGap, 0);
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

    private Task ShowPagedCollectionAsync(string title, Track? artwork, string kind,
        Func<int, CancellationToken, Task<(IReadOnlyList<Track> Tracks, bool HasMore, Action? Advance)>> load, bool replace = false, DiagnosticSector sector = DiagnosticSector.Catalog)
    {
        var tracks = new AvaloniaList<Track>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pageIndex = 0;
        OpenCollection(title, tracks, kind, artwork, replace: replace, pagedBody: scroll =>
            BuildPagedCollectionBody(scroll, () => tracks.Count > 0, async token =>
            {
                var (batch, hasMore, advance) = await load(pageIndex, token);
                token.ThrowIfCancellationRequested();
                var next = batch.Where(t => !t.IsArtist && seen.Add(t.Source))
                    .Select(t => t.Artist == "YouTube" && artwork is not null ? t with { Artist = artwork.Artist } : t).ToArray();
                advance?.Invoke();
                pageIndex++;
                tracks.AddRange(next);
                // A continuation can point to a page of tracks already seen. Offset-based sources cannot recover
                // from a provider returning the same page indefinitely.
                return hasMore && (advance is not null || next.Length > 0);
            }, sector, "Nenhuma faixa disponível"));
        return Task.CompletedTask;
    }

    private Control BuildPagedCollectionBody(ScrollViewer scroll, Func<bool> hasItems,
        Func<CancellationToken, Task<bool>> load, DiagnosticSector sector, string emptyMessage)
    {
        var message = new ContentControl { VerticalContentAlignment = VerticalAlignment.Center };
        var body = new Grid { RowDefinitions = new("Auto,*") };
        body.Children.Add(message);
        Put(body, scroll, 1, 0);
        var hasMore = true;
        var needsRetry = false;
        var loading = false;
        CancellationTokenSource? request = null;

        void CheckRemaining()
        {
            if (!hasMore || needsRetry || loading || TopLevel.GetTopLevel(scroll) is null || !scroll.IsEffectivelyVisible) return;
            if (!hasItems() || scroll.Viewport.Height > 0
                && scroll.Extent.Height - scroll.Offset.Y - scroll.Viewport.Height <= scroll.Viewport.Height * .6)
                _ = LoadAsync();
        }

        async Task LoadAsync()
        {
            if (!hasMore || loading || TopLevel.GetTopLevel(scroll) is null || !scroll.IsEffectivelyVisible) return;
            if (!_music.CanUseOnline)
            {
                ShowRetry("Disponível no modo online");
                return;
            }

            needsRetry = false;
            loading = true;
            using var cancellation = new CancellationTokenSource();
            request = cancellation;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            message.Content = hasItems() ? null : Text("Carregando…", UiTheme.Caption, Muted);
            try
            {
                hasMore = await load(timeout.Token);
                if (!hasMore && !hasItems()) message.Content = Text(emptyMessage, UiTheme.Body, Muted);
                else message.Content = null;
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                PlaybackDiagnostics.WriteFailure(sector, "Carregar página da coleção", error);
                if (TopLevel.GetTopLevel(scroll) is not null) ShowRetry("Conteúdo indisponível");
            }
            finally
            {
                request = null;
                loading = false;
                // Recheck after layout, including when a short or duplicate-only page didn't fill the viewport.
                Dispatcher.UIThread.Post(CheckRemaining, DispatcherPriority.Loaded);
            }
        }

        void ShowRetry(string label)
        {
            needsRetry = true;
            var retry = CollectionAction("Tentar novamente", LoadAsync);
            message.Content = new WrapPanel
            {
                ItemSpacing = UiTheme.CompactGap,
                Children = { Text(label, UiTheme.Caption, Muted), retry }
            };
        }

        scroll.ScrollChanged += (_, e) =>
        {
            if (ReferenceEquals(e.Source, scroll)) CheckRemaining();
        };
        scroll.AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(CheckRemaining, DispatcherPriority.Loaded);
        scroll.DetachedFromVisualTree += (_, _) => request?.Cancel();
        return body;
    }

    private Task ShowChannelPlaylistsAsync(Track artist)
    {
        var index = 0;
        var playlists = new List<Track>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rows = new AvaloniaList<Track[]>();
        var columns = 1;
        var cardSize = _androidLayout ? MobileHomeCardSize : 196;
        var cards = CollectionList<Track[]>(items =>
        {
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

        OpenCollection(artist.Title, [], "Playlists do canal", artist, pagedBody: scroll =>
        {
            scroll.Content = cards;
            scroll.SizeChanged += (_, e) =>
            {
                var width = e.NewSize.Width;
                if (width <= 0 || width == e.PreviousSize.Width) return;
                var size = Math.Min(_androidLayout ? MobileHomeCardSize : 196, width);
                var count = Math.Max(1, (int)((width + UiTheme.PageGap) / (size + UiTheme.PageGap)));
                if (count == columns && size == cardSize) return;
                cardSize = size;
                columns = count;
                rows.Clear();
                rows.AddRange(playlists.Chunk(columns));
            };
            return BuildPagedCollectionBody(scroll, () => playlists.Count > 0, async token =>
            {
                var batch = await _music.BrowsePageAsync(artist.Source + "/playlists", index, token);
                token.ThrowIfCancellationRequested();
                var next = batch.Where(track => seen.Add(track.Id)).ToArray();
                if (next.Length == 0) return false;
                var first = playlists.Count / columns * columns;
                if (first < playlists.Count) rows.RemoveAt(rows.Count - 1);
                playlists.AddRange(next);
                rows.AddRange(playlists.Skip(first).Chunk(columns));
                index++;
                return batch.Count >= 50;
            }, DiagnosticSector.Catalog, "Nenhuma playlist pública neste canal.");
        });
        return Task.CompletedTask;
    }
}
