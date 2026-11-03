using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Hibike.Core.Models;
using Hibike.Core.Services;

namespace Hibike.App.Views;

public sealed partial class MainView
{
    private enum LibraryTab { Songs, Playlists, Albums, Artists }
    private enum LibrarySource { All, Local, Online }
    private enum LibraryOrder { Recent, Title, Artist }
    private sealed record LibraryGroup(string Title, string Subtitle, Track? Artwork, IReadOnlyList<Track> Tracks,
        MusicController.LocalPlaylist? Playlist = null, AccountPlaylist? OnlinePlaylist = null);

    private static readonly string[] LibraryTabLabels = ["Músicas", "Playlists", "Álbuns", "Artistas"];
    private LibraryTab _mobileLibraryTab;
    private LibrarySource _mobileLibrarySource;
    private LibraryOrder _mobileLibraryOrder;
    private LibraryGroup? _mobileLibraryGroup;
    private readonly List<Button> _mobileLibraryTabs = [];
    private readonly List<Border> _mobileLibraryIndicators = [];
    private readonly TextBox _mobileLibrarySearch = UiTheme.Field("Buscar na biblioteca");
    private TextBlock _mobileLibraryTitle = null!, _mobileLibrarySummary = null!;
    private Button _mobileLibraryBack = null!, _mobileLibraryAdd = null!, _mobileLibraryPlay = null!;
    private Grid _mobileLibraryTabRail = null!, _mobileLibraryTools = null!;
    private ContentControl _mobileLibraryHero = null!, _mobileLibraryEmpty = null!;
    private ItemsControl _mobileLibraryTracks = null!, _mobileLibraryGroups = null!;
    private ScrollViewer _mobileLibraryScroll = null!;
    private string? _mobileLibraryGroupKey;
    private Track[] _mobileLibraryShownTracks = [];
    private LibraryGroup? _mobileLibraryRenderedGroup;
    private Vector _mobileLibraryGroupOffset;
    private TextBlock _mobileLibraryError = null!;

    private Grid BuildMobileLibrary()
    {
        var page = new Grid { RowDefinitions = new("Auto,Auto,Auto,Auto,Auto,Auto,*"), RowSpacing = 10 };
        var header = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 4, Margin = new Thickness(0, 4, 0, 0) };
        _mobileLibraryBack = CollectionIcon("back", "Voltar à biblioteca", () => { CloseMobileLibraryGroup(); return Task.CompletedTask; });
        header.Children.Add(_mobileLibraryBack);
        _mobileLibraryTitle = CollectionText("Sua biblioteca", 28, weight: FontWeight.SemiBold);
        Put(header, _mobileLibraryTitle, 0, 1);
        _mobileLibraryAdd = CollectionIcon("folder", "Importar músicas", RunMobileLibraryHeaderActionAsync);
        Put(header, _mobileLibraryAdd, 0, 2);
        page.Children.Add(header);

        _mobileLibraryTabRail = new Grid { ColumnDefinitions = new("*,*,*,*"), RowDefinitions = new("Auto,2") };
        var rule = CollectionRule();
        Put(_mobileLibraryTabRail, rule, 1, 0);
        Grid.SetColumnSpan(rule, 4);
        for (var index = 0; index < LibraryTabLabels.Length; index++)
        {
            var tab = (LibraryTab)index;
            var label = LibraryTabLabels[index];
            var button = ActionButton("", () =>
            {
                if (_mobileLibraryTab != tab)
                {
                    _mobileLibraryTab = tab;
                    _mobileLibraryScroll.Offset = default;
                    RefreshMobileLibrary();
                }
                return Task.CompletedTask;
            });
            button.Classes.Add("subtle");
            button.Padding = UiTheme.Zero;
            AutomationProperties.SetName(button, label);
            _mobileLibraryTabs.Add(button);
            Put(_mobileLibraryTabRail, button, 0, index);
            var indicator = new Border { Height = 2, CornerRadius = new(1), Margin = new Thickness(12, 0),
                Background = UiTheme.Brush(CollectionAccent) };
            _mobileLibraryIndicators.Add(indicator);
            Put(_mobileLibraryTabRail, indicator, 1, index);
        }
        Put(page, _mobileLibraryTabRail, 1, 0);

        _mobileLibraryTools = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 };
        _mobileLibrarySearch.InnerLeftContent = new Border { Child = Icons.Make("search", Muted, 19), Padding = new Thickness(12, 0, 8, 0) };
        _mobileLibrarySearch.TextChanged += (_, _) => { _mobileLibraryScroll.Offset = default; RefreshMobileLibrary(); };
        _mobileLibraryTools.Children.Add(_mobileLibrarySearch);
        Put(_mobileLibraryTools, CollectionIcon("sort", "Ordenar e filtrar biblioteca", () =>
        {
            ShowMobileLibraryFilters();
            return Task.CompletedTask;
        }), 0, 1);
        Put(page, _mobileLibraryTools, 2, 0);

        _mobileLibraryHero = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch };
        Put(page, _mobileLibraryHero, 3, 0);
        var summary = new Grid { ColumnDefinitions = new("*,Auto") };
        _mobileLibrarySummary = CollectionText("", UiTheme.Caption, Muted, FontWeight.Medium);
        summary.Children.Add(_mobileLibrarySummary);
        _mobileLibraryPlay = CollectionAction("Tocar tudo", () => PlayCollectionAsync(_mobileLibraryShownTracks), prominent: true);
        Put(summary, _mobileLibraryPlay, 0, 1);
        Put(page, summary, 4, 0);

        _mobileLibraryTracks = CollectionList<Track>(track => CollectionTrackRow(track,
            () => PlayMobileLibraryTrackAsync(track), anchor => ShowSongMenu(track, anchor), highlightCurrent: true));
        _mobileLibraryGroups = CollectionList<LibraryGroup[]>(BuildMobileLibraryGroupRow);
        _mobileLibraryScroll = UiTheme.FadingScroll();
        _mobileLibraryEmpty = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch };
        _mobileLibraryError = CollectionText("", UiTheme.Caption, UiTheme.Error);
        _mobileLibraryError.TextWrapping = TextWrapping.Wrap;
        _mobileLibraryError.MaxLines = 2;
        Put(page, _mobileLibraryError, 5, 0);
        Put(page, new Grid { Children = { _mobileLibraryScroll, _mobileLibraryEmpty } }, 6, 0);

        return page;
    }

    private void RefreshMobileLibrary()
    {
        if (_mobileLibraryGroup?.Playlist is { } selected && !_music.Playlists.Contains(selected)) _mobileLibraryGroup = null;
        var detail = _mobileLibraryGroup;
        _mobileLibraryError.Text = _music.Status;
        _mobileLibraryError.IsVisible = _music.HasError;
        _mobileLibraryBack.IsVisible = detail is not null;
        _mobileLibraryTitle.Text = detail is null ? "Sua biblioteca" : detail.Playlist?.Name ?? detail.Title;
        _mobileLibraryTabRail.IsVisible = _mobileLibraryTools.IsVisible = detail is null;

        for (var index = 0; index < _mobileLibraryTabs.Count; index++)
        {
            var active = (int)_mobileLibraryTab == index;
            var tab = _mobileLibraryTabs[index];
            if (tab.Content is not TextBlock || _mobileLibraryIndicators[index].IsVisible != active)
                tab.Content = CollectionText(LibraryTabLabels[index], UiTheme.Caption, active ? CollectionAccent : Muted,
                    active ? FontWeight.SemiBold : FontWeight.Medium);
            _mobileLibraryIndicators[index].IsVisible = active;
        }

        var icon = detail?.Playlist is not null ? "more" : detail is null && _mobileLibraryTab == LibraryTab.Playlists ? "plus" : "folder";
        var actionLabel = icon switch { "more" => "Gerenciar playlist", "plus" => "Nova playlist", _ => "Importar músicas" };
        if (AutomationProperties.GetName(_mobileLibraryAdd) != actionLabel)
        {
            _mobileLibraryAdd.Content = Icons.Make(icon);
            AutomationProperties.SetName(_mobileLibraryAdd, actionLabel);
            ToolTip.SetTip(_mobileLibraryAdd, actionLabel);
        }
        _mobileLibraryAdd.IsVisible = detail is null || detail.Playlist is not null;

        if (!ReferenceEquals(_mobileLibraryRenderedGroup, detail))
        {
            _mobileLibraryRenderedGroup = detail;
            _mobileLibraryHero.Content = detail is null ? null : BuildMobileLibraryHero(detail);
        }
        _mobileLibraryHero.IsVisible = detail is not null;

        var query = _mobileLibrarySearch.Text?.Trim() ?? "";
        var tracks = detail is null ? FilterMobileLibraryTracks().ToArray()
            : detail.Playlist?.Tracks.ToArray() ?? detail.Tracks.Where(_music.IsFavorite).ToArray();
        var showingTracks = detail is not null || _mobileLibraryTab == LibraryTab.Songs;
        var count = showingTracks ? tracks.Length : 0;

        if (showingTracks)
        {
            if (detail is null && query.Length > 0)
                tracks = tracks.Where(track => MatchesLibraryQuery($"{track.Title} {track.Artist} {track.Album}", query)).ToArray();
            if (detail is null) tracks = OrderMobileLibraryTracks(tracks).ToArray();
            if (!_mobileLibraryShownTracks.SequenceEqual(tracks))
            {
                _mobileLibraryShownTracks = tracks;
                _mobileLibraryTracks.ItemsSource = tracks;
            }
            count = tracks.Length;
            _mobileLibrarySummary.Text = TrackCount(count);
        }
        else
        {
            var groups = MobileLibraryGroups(tracks, query).ToArray();
            count = groups.Length;
            var key = _mobileLibraryTab + ":" + string.Join("|", groups.Select(group => $"{group.Title}\n{group.Subtitle}\n{group.Artwork?.Thumbnail}\n{group.OnlinePlaylist?.Id}\n{string.Join(",", group.Tracks.Select(track => track.Source))}"));
            if (_mobileLibraryGroupKey != key)
            {
                _mobileLibraryGroupKey = key;
                _mobileLibraryGroups.ItemsSource = groups.Chunk(2).ToArray();
            }
            var unit = _mobileLibraryTab switch
            {
                LibraryTab.Albums => count == 1 ? "álbum" : "álbuns",
                LibraryTab.Artists => count == 1 ? "artista" : "artistas",
                _ => count == 1 ? "playlist" : "playlists"
            };
            _mobileLibrarySummary.Text = $"{count} {unit}";
        }

        var items = showingTracks ? _mobileLibraryTracks : _mobileLibraryGroups;
        if (!ReferenceEquals(_mobileLibraryScroll.Content, items)) _mobileLibraryScroll.Content = items;
        _mobileLibraryPlay.IsVisible = showingTracks && count > 0;
        _mobileLibraryScroll.IsVisible = count > 0;
        _mobileLibraryEmpty.IsVisible = count == 0;
        if (count > 0) return;

        var filtered = detail is null && (query.Length > 0 || _mobileLibrarySource != LibrarySource.All);
        var canCreate = !filtered && detail is null && _mobileLibraryTab is LibraryTab.Songs or LibraryTab.Playlists;
        var playlists = _mobileLibraryTab == LibraryTab.Playlists;
        var title = filtered ? "Nenhum resultado" : detail is not null ? "Nenhuma faixa nesta coleção"
            : playlists ? "Nenhuma playlist" : "Sua biblioteca está vazia aqui";
        var message = filtered ? "Tente outra busca ou ajuste os filtros."
            : detail is not null ? "As faixas desta coleção aparecem aqui."
            : playlists ? "Crie uma playlist e adicione músicas pelo menu de cada faixa."
            : "Importe músicas ou salve faixas nos favoritos para montar sua biblioteca.";
        if (!Equals(_mobileLibraryEmpty.Tag, (title, message, canCreate)))
        {
            _mobileLibraryEmpty.Tag = (title, message, canCreate);
            _mobileLibraryEmpty.Content = CollectionEmpty(playlists ? "queue" : "library", title, message,
                canCreate ? CollectionAction(playlists ? "Nova playlist" : "Importar músicas", RunMobileLibraryHeaderActionAsync, prominent: true) : null);
        }
    }

    private IEnumerable<Track> FilterMobileLibraryTracks() => _music.Favorites.Where(track => _mobileLibrarySource switch
    {
        LibrarySource.Local => track.IsLocal,
        LibrarySource.Online => !track.IsLocal,
        _ => true
    });

    private IEnumerable<Track> OrderMobileLibraryTracks(IEnumerable<Track> tracks) => _mobileLibraryOrder switch
    {
        LibraryOrder.Title => tracks.OrderBy(track => track.Title, StringComparer.CurrentCultureIgnoreCase),
        LibraryOrder.Artist => tracks.OrderBy(track => track.Artist, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(track => track.Title, StringComparer.CurrentCultureIgnoreCase),
        _ => tracks.OrderByDescending(track => _music.Listening.GetValueOrDefault(MusicController.SongKey(track))?.LastPlayed ?? DateTime.MinValue)
    };

    private IEnumerable<LibraryGroup> MobileLibraryGroups(Track[] tracks, string query)
    {
        if (_mobileLibraryTab == LibraryTab.Playlists)
        {
            var local = _music.Playlists.Where(playlist => _mobileLibrarySource != LibrarySource.Online && MatchesLibraryQuery(playlist.Name, query))
                .Select(playlist => new LibraryGroup(playlist.Name, TrackCount(playlist.Tracks.Count),
                    playlist.Tracks.FirstOrDefault(track => track.Thumbnail is not null), playlist.Tracks, playlist));
            var online = _accountPlaylists.Where(playlist => _mobileLibrarySource != LibrarySource.Local && MatchesLibraryQuery(playlist.Title, query))
                .Select(playlist => new LibraryGroup(playlist.Title, "YouTube",
                    new Track(playlist.Id, playlist.Title, "YouTube", playlist.Id, Thumbnail: playlist.Thumbnail), [], OnlinePlaylist: playlist));
            return local.Concat(online).OrderBy(group => group.Title, StringComparer.CurrentCultureIgnoreCase);
        }

        var byArtist = _mobileLibraryTab == LibraryTab.Artists;
        return tracks.Where(track => byArtist || !string.IsNullOrWhiteSpace(track.Album))
            .GroupBy(track => (Artist: track.Artist, Album: byArtist ? null : track.Album))
            .Where(group => MatchesLibraryQuery($"{group.Key.Artist} {group.Key.Album}", query))
            .Select(group => new LibraryGroup(byArtist ? TrackTextFormatter.Artist(group.Key.Artist) : group.Key.Album!,
                byArtist ? TrackCount(group.Count()) : TrackTextFormatter.Artist(group.Key.Artist),
                group.FirstOrDefault(track => track.Thumbnail is not null), group.ToArray()))
            .OrderBy(group => _mobileLibraryOrder == LibraryOrder.Artist ? group.Tracks.FirstOrDefault()?.Artist : group.Title,
                StringComparer.CurrentCultureIgnoreCase);
    }

    private Control BuildMobileLibraryGroupRow(LibraryGroup[] groups)
    {
        var row = new Grid { ColumnDefinitions = new("*,*"), ColumnSpacing = 14, Margin = new Thickness(0, 0, 0, 14) };
        for (var index = 0; index < groups.Length; index++)
        {
            var group = groups[index];
            var artwork = new Artwork(group.Artwork, 160, stretch: Stretch.Uniform, highQuality: true)
            {
                HorizontalAlignment = HorizontalAlignment.Center, CornerRadius = new(12)
            };
            var labels = new StackPanel { Spacing = 3, Margin = new Thickness(0, 3, 0, 0) };
            labels.Children.Add(CollectionText(group.Title, UiTheme.Body, weight: FontWeight.SemiBold));
            labels.Children.Add(CollectionText(group.Subtitle, UiTheme.Caption, Muted));
            var tile = new StackPanel { Spacing = 6, Children = { artwork, labels } };
            var button = ActionButton("", () => OpenMobileLibraryGroupAsync(group));
            button.Classes.Add("subtle");
            button.Padding = UiTheme.Zero;
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            button.Content = tile;
            button.SizeChanged += (_, _) =>
            {
                var size = Math.Min(240, Math.Floor(button.Bounds.Width));
                if (size > 0 && artwork.Width != size) artwork.Width = artwork.Height = size;
            };
            AutomationProperties.SetName(button, group.Title);
            Put(row, button, 0, index);
        }
        return row;
    }

    private Control BuildMobileLibraryHero(LibraryGroup group)
    {
        var hero = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 16 };
        hero.Children.Add(new Artwork(group.Artwork, 86, stretch: Stretch.Uniform, highQuality: true));
        var labels = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        var kind = group.Playlist is not null ? "Playlist" : _mobileLibraryTab == LibraryTab.Albums ? "Álbum" : "Artista";
        labels.Children.Add(CollectionText(kind, UiTheme.Caption, CollectionAccent));
        labels.Children.Add(CollectionText(group.Playlist is not null ? "Sua coleção" : group.Subtitle, UiTheme.Body, Muted));
        Put(hero, labels, 0, 1);
        return hero;
    }

    private Task OpenMobileLibraryGroupAsync(LibraryGroup group)
    {
        if (group.OnlinePlaylist is { } playlist && _account is { } account)
            return ShowAccountTracksAsync(account, playlist.Id, playlist.Title);
        _mobileLibraryGroupOffset = _mobileLibraryScroll.Offset;
        _mobileLibraryGroup = group;
        _mobileLibraryScroll.Offset = default;
        RefreshMobileLibrary();
        return Task.CompletedTask;
    }

    private void CloseMobileLibraryGroup()
    {
        _mobileLibraryGroup = null;
        RefreshMobileLibrary();
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_mobileLibraryGroup is null) _mobileLibraryScroll.Offset = _mobileLibraryGroupOffset;
        }, Avalonia.Threading.DispatcherPriority.Render);
    }

    private Task RunMobileLibraryHeaderActionAsync()
    {
        if (_mobileLibraryGroup?.Playlist is { } playlist) ShowMobilePlaylistOptions(playlist);
        else if (_mobileLibraryTab == LibraryTab.Playlists) ShowNewPlaylist();
        else return OpenLocalAsync();
        return Task.CompletedTask;
    }

    private Task PlayMobileLibraryTrackAsync(Track track)
    {
        var index = _music.Queue.IndexOf(track);
        return _music.PlayAsync(track, index >= 0 ? index : null);
    }

    private void ShowMobileLibraryFilters()
    {
        var panel = new StackPanel { Spacing = 12 };
        var source = new ComboBox { ItemsSource = new[] { "Todas as fontes", "No dispositivo", "Online" },
            SelectedIndex = (int)_mobileLibrarySource, HorizontalAlignment = HorizontalAlignment.Stretch };
        var order = new ComboBox { ItemsSource = new[] { "Tocadas recentemente", "Título A–Z", "Artista A–Z" },
            SelectedIndex = (int)_mobileLibraryOrder, HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = _mobileLibraryTab == LibraryTab.Songs };
        panel.Children.Add(CollectionText("Origem das faixas", UiTheme.Caption, Muted));
        panel.Children.Add(source);
        panel.Children.Add(CollectionText("Ordenação", UiTheme.Caption, Muted));
        panel.Children.Add(order);
        panel.Children.Add(CollectionAction("Aplicar", () =>
        {
            _mobileLibrarySource = (LibrarySource)Math.Max(0, source.SelectedIndex);
            _mobileLibraryOrder = (LibraryOrder)Math.Max(0, order.SelectedIndex);
            _mobileLibraryScroll.Offset = default;
            CloseSheet();
            RefreshMobileLibrary();
            return Task.CompletedTask;
        }, prominent: true));
        ShowSheet("Filtros da biblioteca", panel);
    }

    private void ShowMobilePlaylistOptions(MusicController.LocalPlaylist playlist)
    {
        var panel = new StackPanel { Spacing = 8 };
        var name = UiTheme.Field("Nome da playlist");
        name.Text = playlist.Name;
        panel.Children.Add(name);
        panel.Children.Add(CollectionAction("Salvar nome", () =>
        {
            _music.RenamePlaylist(playlist, name.Text ?? "");
            CloseSheet();
            return Task.CompletedTask;
        }, prominent: true));
        panel.Children.Add(CollectionAction(playlist.Pinned ? "Desafixar playlist" : "Fixar playlist", () =>
        {
            _music.PinPlaylist(playlist);
            CloseSheet();
            return Task.CompletedTask;
        }));
        panel.Children.Add(CollectionAction("Excluir playlist…", () =>
        {
            ShowSheet("Excluir playlist", CollectionAction("Confirmar exclusão", () =>
            {
                _music.DeletePlaylist(playlist);
                CloseSheet();
                return Task.CompletedTask;
            }));
            return Task.CompletedTask;
        }));
        ShowSheet("Gerenciar playlist", panel);
    }

    private static bool MatchesLibraryQuery(string value, string query) =>
        value.Contains(query, StringComparison.CurrentCultureIgnoreCase);

    private static string TrackCount(int count) => count == 1 ? "1 faixa" : $"{count} faixas";
}
