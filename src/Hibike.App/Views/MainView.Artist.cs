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

    private void ShowArtistCatalog(MusicArtist artist, ArtistProfile profile)
    {
        var artwork = new Track(artist.Id, artist.Name, artist.Name, "https://www.youtube.com/channel/" + artist.Id, Thumbnail: profile.Image ?? artist.Image);
        OpenCollection(artist.Name, profile.Tracks, "Artista · destaques", artwork);
        var grid = (Grid)_collectionPage.Child!;
        var body = new StackPanel { Spacing = UiTheme.SectionGap, Margin = new Thickness(0, UiTheme.PageGap) };
        var videos = ActionButton("Ver todos os vídeos", () => ShowPagedCollectionAsync(artist.Name, artwork.Source + "/videos", artwork, "Vídeos"));
        var navigation = new WrapPanel { ItemSpacing = UiTheme.CompactGap, LineSpacing = UiTheme.CompactGap };
        navigation.Children.Add(videos);
        navigation.Children.Add(ActionButton("Playlists do canal", () => ShowChannelPlaylistsAsync(artwork)));
        body.Children.Add(navigation);
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
        foreach (var kind in profile.Collections.Select(c => c.Kind).Distinct())
        {
            var collections = profile.Collections.Where(c => c.Kind == kind).ToArray();
            if (collections.Length == 0) continue;
            var section = profile.Sections.FirstOrDefault(s => kind == "Single / EP" ? s.Title.Contains("Singles") : kind == "Álbum" ? s.Title.Contains("Albums") : s.Title.Contains("Playlists"));
            body.Children.Add(ArtistShelf(kind == "Álbum" ? "Álbuns" : kind == "Single / EP" ? "Singles e EPs" : "Playlists", collections,
                c => OpenArtistCollectionAsync(c, artwork), section is null ? null : () => ShowMusicSectionAsync(artist, section)));
        }
        foreach (var section in profile.Sections.Where(s => s.Title.Contains("parcial", StringComparison.OrdinalIgnoreCase)))
            body.Children.Add(ActionButton(section.Title, () => ShowMusicSectionAsync(artist, section)));
        var previous = grid.Children.Single(c => Grid.GetRow(c) == 2);
        grid.Children.Remove(previous);
        Put(grid, UiTheme.FadingScroll(body), 2, 0);
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
            var card = CoverCard(new(c.Id, c.Title, title, "", Thumbnail: c.Image), c.Kind, () => open(c), songActions: false,
                cardSize: _androidLayout ? MobileHomeCardSize : 196);
            card.Margin = new Thickness(0, 0, UiTheme.PageGap, 0);
            return card;
        });
        cards.ItemsPanel = new Avalonia.Controls.Templates.FuncTemplate<Panel?>(() => new VirtualizingStackPanel { Orientation = Orientation.Horizontal });
        cards.ItemsSource = entries.ToArray(); scroll.Content = cards; section.Children.Add(scroll);
        return section;
    }

    private async Task ShowMusicSectionAsync(MusicArtist artist, ArtistSection section)
    {
        var artwork = new Track(artist.Id, artist.Name, artist.Name, "https://www.youtube.com/channel/" + artist.Id, Thumbnail: artist.Image);
        if (section.Id.StartsWith("VL", StringComparison.Ordinal))
        {
            await ShowPagedCollectionAsync(section.Title, "https://www.youtube.com/playlist?list=" + Uri.EscapeDataString(section.Id[2..]), artwork, "Catálogo musical");
            return;
        }
        OpenCollection(section.Title, [], "Catálogo musical", artwork, "Buscando catálogo completo…");
        var page = _collectionPage.Child;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var profile = await MusicDiscovery.ArtistSectionAsync(section, timeout.Token);
            if (_page != "collection" || !ReferenceEquals(page, _collectionPage.Child)) return;
            ShowArtistCatalog(artist, profile);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            PlaybackDiagnostics.Write("Discografia", e.Message);
            if (_page == "collection" && ReferenceEquals(page, _collectionPage.Child))
                OpenCollection(section.Title, [], "Catálogo musical", artwork, "Catálogo indisponível", () => ShowMusicSectionAsync(artist, section));
        }
    }

    private async Task OpenArtistCollectionAsync(ArtistCollection collection, Track artist)
    {
        var playlist = collection.Id.StartsWith("VL", StringComparison.Ordinal) ? collection.Id[2..] : null;
        if (playlist is null)
        {
            OpenCollection(collection.Title, [], collection.Kind, artist, "Buscando álbum…");
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
                    OpenCollection(collection.Title, [], collection.Kind, artist, "Álbum indisponível", () => OpenArtistCollectionAsync(collection, artist));
                return;
            }
        }
        await ShowPagedCollectionAsync(collection.Title, "https://www.youtube.com/playlist?list=" + Uri.EscapeDataString(playlist),
            artist with { Thumbnail = collection.Image ?? artist.Thumbnail }, collection.Kind);
    }

    private async Task ShowPagedCollectionAsync(string title, string source, Track artwork, string kind)
    {
        var tracks = new List<Track>();
        var pageIndex = 0;
        OpenCollection(title, [], kind, artwork, "Buscando conteúdo…");
        var page = _collectionPage.Child;
        var more = ActionButton("Carregar mais", () => Task.CompletedTask);
        more.Click += async (_, _) => await LoadAsync();
        await LoadAsync();

        async Task LoadAsync()
        {
            if (!more.IsEnabled) return;
            more.IsEnabled = false;
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                var batch = await _music.BrowsePageAsync(source, pageIndex, timeout.Token);
                if (_page != "collection" || !ReferenceEquals(page, _collectionPage.Child)) return;
                tracks.AddRange(batch.Where(t => !t.IsArtist).Select(t => t.Artist == "YouTube" ? t with { Artist = artwork.Artist } : t));
                pageIndex++;
                if (more.Parent is Panel parent) parent.Children.Remove(more);
                OpenCollection(title, tracks.DistinctBy(t => t.Source).ToArray(), kind, artwork);
                page = _collectionPage.Child;
                if (batch.Count >= 50)
                {
                    var actions = (WrapPanel)((Grid)page!).Children.Single(c => Grid.GetRow(c) == 1);
                    more.Content = "Carregar mais";
                    actions.Children.Add(more);
                }
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                PlaybackDiagnostics.Write("Catálogo paginado", e.Message);
                if (_page != "collection" || !ReferenceEquals(page, _collectionPage.Child)) return;
                if (tracks.Count == 0)
                {
                    OpenCollection(title, [], kind, artwork, "Conteúdo indisponível", LoadAsync);
                    page = _collectionPage.Child;
                }
                else more.Content = "Falha ao carregar · tentar novamente";
            }
            finally { more.IsEnabled = true; }
        }
    }

    private async Task ShowChannelPlaylistsAsync(Track artist)
    {
        OpenCollection(artist.Title, [], "Playlists do canal", artist, "Buscando playlists…");
        var page = _collectionPage.Child;
        var index = 0;
        var playlists = new List<Track>();
        var rows = new System.Collections.ObjectModel.ObservableCollection<Track[]>();
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
        var scroll = UiTheme.FadingScroll(cards);
        var more = ActionButton("Carregar mais playlists", () => Task.CompletedTask);
        more.IsVisible = false;
        var content = new Grid { RowDefinitions = new("*,Auto"), RowSpacing = UiTheme.CompactGap };
        content.Children.Add(scroll);
        Put(content, more, 1, 0);
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
            rows.Clear();
            AppendRows(0);
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
                var first = playlists.Count / columns * columns;
                if (first < playlists.Count) rows.RemoveAt(rows.Count - 1);
                playlists.AddRange(batch);
                AppendRows(first);
                index++;
                more.IsVisible = batch.Count >= 50;
                if (index != 1) return;
                var grid = (Grid)page!;
                foreach (var child in grid.Children.Where(c => Grid.GetRow(c) == 2).ToArray()) grid.Children.Remove(child);
                Put(grid, playlists.Count == 0 ? Text("Nenhuma playlist pública neste canal.", UiTheme.Body) : content, 2, 0);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                PlaybackDiagnostics.Write("Playlists do canal", e.Message);
                if (_page == "collection" && ReferenceEquals(page, _collectionPage.Child))
                {
                    if (playlists.Count == 0)
                    {
                        OpenCollection(artist.Title, [], "Playlists do canal", artist, "Playlists indisponíveis", LoadAsync);
                        page = _collectionPage.Child;
                    }
                    else more.Content = "Falha ao carregar · tentar novamente";
                }
            }
            finally { more.IsEnabled = true; }
        }
    }
}
