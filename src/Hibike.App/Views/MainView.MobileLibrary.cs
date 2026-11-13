using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
    private LibraryTab _mobileLibraryRenderedTab;
    private LibrarySource _mobileLibrarySource;
    private LibraryOrder _mobileLibraryOrder;
    private LibraryGroup? _mobileLibraryGroup;
    private readonly List<Button> _mobileLibraryTabs = [];
    private readonly TextBox _mobileLibrarySearch = UiTheme.Field("Buscar na biblioteca");
    private TextBlock _mobileLibraryTitle = null!, _mobileLibrarySummary = null!, _mobileLibrarySection = null!;
    private Button _mobileLibraryBack = null!, _mobileLibraryAdd = null!, _mobileLibraryPlay = null!;
    private bool _mobileLibraryListMode;
    private Button _mobileLibraryViewMode = null!;
    private TextBlock _mobileLibraryFilterSummary = null!;
    private Border _mobileLibraryTabRail = null!;
    private Grid _mobileLibraryTools = null!;
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
        var page = new Grid { RowDefinitions = new("Auto,Auto,Auto,Auto,Auto,Auto,*"),
            RowSpacing = UiTheme.CompactGap, ClipToBounds = true };
        var header = new Grid { ColumnDefinitions = new("Auto,*,Auto") };
        _mobileLibraryBack = IconButton("back", "Voltar à biblioteca", () => { CloseMobileLibraryGroup(); return Task.CompletedTask; });
        header.Children.Add(_mobileLibraryBack);
        _mobileLibraryTitle = CollectionText("Sua biblioteca", UiTheme.PageTitle, weight: FontWeight.SemiBold);
        Put(header, _mobileLibraryTitle, 0, 1);
        _mobileLibraryAdd = IconButton("folder", "Importar músicas", RunMobileLibraryHeaderActionAsync);
        Put(header, _mobileLibraryAdd, 0, 2);
        page.Children.Add(header);

        var tabs = new Grid { ColumnDefinitions = new("*,*,*,*"), ColumnSpacing = 2 };
        for (var index = 0; index < LibraryTabLabels.Length; index++)
        {
            var tab = (LibraryTab)index;
            var label = LibraryTabLabels[index];
            var button = ActionButton(label, () =>
            {
                if (_mobileLibraryTab != tab)
                {
                    RevealMobileTopBars();
                    EndTrackSelection();
                    _mobileLibraryTab = tab;
                    _mobileLibraryScroll.Offset = default;
                    RefreshMobileLibrary();
                    Animate(_mobileLibraryScroll, 6);
                }
                return Task.CompletedTask;
            });
            button.Classes.Add("subtle");
            button.Padding = new Thickness(2, 0);
            AutomationProperties.SetName(button, label);
            _mobileLibraryTabs.Add(button);
            Put(tabs, button, 0, index);
        }
        _mobileLibraryTabRail = CollectionSegments(tabs);
        Put(page, _mobileLibraryTabRail, 1, 0);

        _mobileLibraryTools = new Grid { ColumnDefinitions = new("*,Auto,Auto"), RowDefinitions = new("Auto,Auto"), ColumnSpacing = 8 };
        AddSearchIcon(_mobileLibrarySearch);
        AutomationProperties.SetName(_mobileLibrarySearch, "Buscar na biblioteca");
        _mobileLibrarySearch.TextChanged += (_, _) => { EndTrackSelection(); RefreshMobileLibrary(); };
        _mobileLibraryTools.Children.Add(_mobileLibrarySearch);
        Grid.SetColumnSpan(_mobileLibrarySearch, 3);
        var clearSearch = CollectionIcon("close", "Limpar busca da biblioteca", () =>
        {
            _mobileLibrarySearch.Clear();
            _mobileLibrarySearch.Focus();
            return Task.CompletedTask;
        });
        clearSearch.IsVisible = false;
        _mobileLibrarySearch.InnerRightContent = clearSearch;
        _mobileLibrarySearch.TextChanged += (_, _) =>
            clearSearch.IsVisible = !string.IsNullOrEmpty(_mobileLibrarySearch.Text);
        Put(_mobileLibraryTools, CollectionIcon("sort", "Ordenar e filtrar biblioteca", () =>
        {
            ShowMobileLibraryFilters();
            return Task.CompletedTask;
        }), 1, 1);
        _mobileLibraryViewMode = CollectionAction("Lista", () =>
        {
            _mobileLibraryListMode = !_mobileLibraryListMode;
            RefreshMobileLibrary();
            return Task.CompletedTask;
        });
        _mobileLibraryViewMode.Padding = new Thickness(8, 0);
        Put(_mobileLibraryTools, _mobileLibraryViewMode, 1, 2);
        Put(page, _mobileLibraryTools, 2, 0);
        _mobileLibraryFilterSummary = CollectionText("", UiTheme.Caption, Muted);
        _mobileLibraryFilterSummary.VerticalAlignment = VerticalAlignment.Center;
        Put(_mobileLibraryTools, _mobileLibraryFilterSummary, 1, 0);

        _mobileLibraryHero = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch };
        Put(page, _mobileLibraryHero, 3, 0);
        var summary = new Grid { ColumnDefinitions = new("*,Auto") };
        _mobileLibrarySection = CollectionText("", UiTheme.SectionTitle, weight: FontWeight.SemiBold);
        _mobileLibrarySummary = CollectionText("", UiTheme.Caption, Muted, FontWeight.Medium);
        summary.Children.Add(new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center,
            Children = { _mobileLibrarySection, _mobileLibrarySummary } });
        _mobileLibraryPlay = CollectionAction("Tocar tudo", () => PlayCollectionAsync(_mobileLibraryShownTracks), prominent: true);
        Put(summary, _mobileLibraryPlay, 0, 1);
        Put(page, summary, 4, 0);

        _mobileLibraryTracks = CollectionList<Track>(track => TrackRow(track, showMenu: true, selectionTracks: () => _mobileLibraryShownTracks));
        _mobileLibraryGroups = CollectionList<LibraryGroup[]>(BuildMobileLibraryGroupRow);
        _mobileLibraryScroll = UiTheme.FadingScroll();
        _mobileLibraryEmpty = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch };
        _mobileLibraryError = CollectionText("", UiTheme.Caption, UiTheme.Error);
        _mobileLibraryError.TextWrapping = TextWrapping.Wrap;
        _mobileLibraryError.MaxLines = 2;
        Put(page, _mobileLibraryError, 5, 0);
        Put(page, new Grid { Children = { _mobileLibraryScroll, _mobileLibraryEmpty } }, 6, 0);

        page.SizeChanged += (_, e) =>
        {
            if (_page != "library" || e.PreviousSize.Width == e.NewSize.Width) return;
            RefreshMobileLibrary();
        };
        MakeTopRowsCollapsible(page, 5);
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
        _mobileLibraryViewMode.IsVisible = detail is null && _mobileLibraryTab != LibraryTab.Songs;
        _mobileLibraryViewMode.Content = _mobileLibraryListMode ? "Grade" : "Lista";
        _mobileLibraryFilterSummary.IsVisible = detail is null;
        var sourceLabel = _mobileLibrarySource switch { LibrarySource.Local => "No dispositivo", LibrarySource.Online => "Online", _ => "Todas as fontes" };
        var orderLabel = _mobileLibraryOrder switch
        {
            LibraryOrder.Artist => "Por artista",
            LibraryOrder.Title => "Título A–Z",
            _ => "Recentes"
        };
        _mobileLibraryFilterSummary.Text = _mobileLibraryTab == LibraryTab.Songs ? sourceLabel + " · " + orderLabel : sourceLabel + " · A–Z";

        for (var index = 0; index < _mobileLibraryTabs.Count; index++)
        {
            var active = (int)_mobileLibraryTab == index;
            var tab = _mobileLibraryTabs[index];
            if (tab.Content is not TextBlock || tab.Classes.Contains("primary") != active)
                tab.Content = CollectionText(LibraryTabLabels[index], UiTheme.Caption, active ? UiTheme.OnPrimary : Muted,
                    active ? FontWeight.SemiBold : FontWeight.Normal);
            tab.Classes.Set("primary", active);
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

        var sameCollection = _mobileLibraryRenderedTab == _mobileLibraryTab && ReferenceEquals(_mobileLibraryRenderedGroup, detail);
        _mobileLibraryRenderedTab = _mobileLibraryTab;
        if (!ReferenceEquals(_mobileLibraryRenderedGroup, detail))
        {
            _mobileLibraryRenderedGroup = detail;
            _mobileLibraryHero.Content = detail is null ? null : BuildMobileLibraryHero(detail);
        }
        _mobileLibraryHero.IsVisible = detail is not null;

        var query = _mobileLibrarySearch.Text?.Trim() ?? "";
        var seed = detail?.Tracks.FirstOrDefault();
        var tracks = detail is null
            ? _mobileLibraryTab == LibraryTab.Playlists ? [] : FilterMobileLibraryTracks().ToArray()
            : detail.Playlist?.Tracks.ToArray() ?? FilterMobileLibraryTracks().Where(track => seed is not null
                && track.Artist == seed.Artist && (_mobileLibraryTab == LibraryTab.Artists || track.Album == seed.Album)).ToArray();
        var showingTracks = detail is not null || _mobileLibraryTab == LibraryTab.Songs;
        _mobileLibrarySection.IsVisible = !showingTracks;
        _mobileLibrarySection.Text = _mobileLibraryTab switch
        {
            LibraryTab.Playlists => "Suas playlists",
            LibraryTab.Albums => "Seus álbuns",
            LibraryTab.Artists => "Seus artistas",
            _ => ""
        };
        var count = showingTracks ? tracks.Length : 0;

        if (showingTracks)
        {
            if (detail is null && query.Length > 0)
                tracks = tracks.Where(track => MatchesLibraryQuery($"{track.Title} {track.Artist} {track.Album}", query)).ToArray();
            if (detail is null) tracks = OrderMobileLibraryTracks(tracks).ToArray();
            if (!_mobileLibraryShownTracks.SequenceEqual(tracks))
            {
                _mobileLibraryShownTracks = tracks;
                UpdateLibraryItems(_mobileLibraryTracks, tracks, sameCollection,
                    track => track.Source);
            }
            count = tracks.Length;
            _mobileLibrarySummary.Text = TrackCount(count);
        }
        else
        {
            var groups = MobileLibraryGroups(tracks, query).ToArray();
            count = groups.Length;
            var key = _mobileLibraryTab + ":" + _mobileLibraryListMode + ":" + LibraryColumns + ":" + MobileLibraryCardSize + ":" + string.Join("|", groups.Select(group => $"{group.Title}\n{group.Subtitle}\n{group.Artwork?.Thumbnail}\n{group.OnlinePlaylist?.Id}\n{string.Join(",", group.Tracks.Select(track => track.Source))}"));
            if (_mobileLibraryGroupKey != key)
            {
                _mobileLibraryGroupKey = key;
                UpdateLibraryItems(_mobileLibraryGroups, groups.Chunk(LibraryColumns).ToArray(), sameCollection,
                    row => GroupIdentity(row[0]), (row, key) => row.Any(group => Equals(GroupIdentity(group), key)));
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
            var empty = UiTheme.Card(EmptyState(playlists ? "queue" : "library", title, message,
                canCreate ? (playlists ? "Nova playlist" : "Importar músicas") : null,
                canCreate ? RunMobileLibraryHeaderActionAsync : null), padding: 0);
            empty.VerticalAlignment = VerticalAlignment.Top;
            _mobileLibraryEmpty.Content = UiTheme.FadingScroll(empty);
        }
    }

    private object GroupIdentity(LibraryGroup group)
    {
        if (group.Playlist is { } playlist) return playlist;
        if (group.OnlinePlaylist is { } online) return online.Id;
        var track = group.Tracks.FirstOrDefault();
        return (track?.Artist, _mobileLibraryTab == LibraryTab.Albums ? track?.Album : null);
    }

    private void UpdateLibraryItems<T>(ItemsControl items, T[] next, bool sameCollection,
        Func<T, object> identity, Func<T, object, bool>? matches = null)
    {
        if (!sameCollection || !ReferenceEquals(_mobileLibraryScroll.Content, items)
            || _mobileLibraryScroll.Offset.Y <= 0 || next.Length == 0)
        {
            items.ItemsSource = next;
            return;
        }

        var anchor = items.GetRealizedContainers()
            .Select(row => (Row: row, Position: row.TranslatePoint(default, _mobileLibraryScroll)))
            .Where(entry => entry.Position is { } position && position.Y + entry.Row.Bounds.Height > 0)
            .OrderBy(entry => entry.Position!.Value.Y).FirstOrDefault();
        var oldIndex = anchor.Row is null ? -1 : items.IndexFromContainer(anchor.Row);
        var oldItem = oldIndex >= 0 ? items.Items[oldIndex] : null;
        items.ItemsSource = next;
        if (oldItem is not T old || anchor.Position is not { } previous) return;

        var key = identity(old);
        var index = Array.FindIndex(next, item => matches?.Invoke(item, key) ?? Equals(identity(item), key));
        if (index < 0) index = Math.Min(oldIndex, next.Length - 1);
        bool StillCurrent() => _page == "library" && ReferenceEquals(_mobileLibraryScroll.Content, items)
            && ReferenceEquals(items.ItemsSource, next);
        Dispatcher.UIThread.Post(() =>
        {
            if (!StillCurrent()) return;
            items.ScrollIntoView(index);
            Dispatcher.UIThread.Post(() =>
            {
                if (!StillCurrent() || items.ContainerFromIndex(index)?.TranslatePoint(default, _mobileLibraryScroll) is not { } current) return;
                var offset = _mobileLibraryScroll.Offset;
                _mobileLibraryScroll.Offset = new Vector(offset.X, offset.Y + current.Y - previous.Y);
            }, DispatcherPriority.Loaded);
        }, DispatcherPriority.Loaded);
    }

    private IEnumerable<Track> FilterMobileLibraryTracks()
    {
        if (!_music.CanUseOnline) return _music.OfflineTracks;
        return _music.OfflineTracks.Concat(_music.Favorites)
            .DistinctBy(track => track.Source)
            .Where(track => _mobileLibrarySource switch
            {
                LibrarySource.Local => track.IsLocal,
                LibrarySource.Online => !track.IsLocal,
                _ => true
            });
    }

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
                .Select(playlist => new LibraryGroup(playlist.Name, (playlist.Pinned ? "Fixada · " : "Local · ") + TrackCount(playlist.Tracks.Count),
                    playlist.Tracks.FirstOrDefault(track => track.Thumbnail is not null), playlist.Tracks, playlist));
            var online = (_music.CanUseOnline ? _accountPlaylists : []).Where(playlist => _mobileLibrarySource != LibrarySource.Local && MatchesLibraryQuery(playlist.Title, query))
                .Select(playlist => new LibraryGroup(playlist.Title, "YouTube",
                    new Track(playlist.Id, playlist.Title, "YouTube", playlist.Id, Thumbnail: playlist.Thumbnail), [], OnlinePlaylist: playlist));
            return local.Concat(online).OrderByDescending(group => group.Playlist?.Pinned == true)
                .ThenBy(group => group.Title, StringComparer.CurrentCultureIgnoreCase);
        }

        var byArtist = _mobileLibraryTab == LibraryTab.Artists;
        return tracks.Where(track => byArtist || !string.IsNullOrWhiteSpace(track.Album))
            .GroupBy(track => (Artist: track.Artist, Album: byArtist ? null : track.Album))
            .Where(group => MatchesLibraryQuery($"{group.Key.Artist} {group.Key.Album}", query))
            .Select(group => new LibraryGroup(byArtist ? TrackTextFormatter.Artist(group.Key.Artist) : group.Key.Album!,
                byArtist ? TrackCount(group.Count()) : TrackTextFormatter.Artist(group.Key.Artist),
                group.FirstOrDefault(track => track.Thumbnail is not null), group.ToArray()))
            .OrderBy(group => group.Title, StringComparer.CurrentCultureIgnoreCase);
    }

    // One column at very narrow widths; additional columns only when cards stay readable.
    private int LibraryColumns => _mobileLibraryListMode ? 1
        : LibraryContentWidth < 500
            ? (LibraryContentWidth >= 300 ? 2 : 1)
            : Math.Clamp((int)Math.Floor((LibraryContentWidth + UiTheme.SectionGap) / 190d), 2, 6);

    private double LibraryContentWidth => _mobileLibrary is { Bounds.Width: > 0 } page ? page.Bounds.Width : 360;

    private double MobileLibraryCardSize =>
        Math.Min(240, Math.Max(1, Math.Floor((LibraryContentWidth - (LibraryColumns - 1) * UiTheme.SectionGap) / LibraryColumns)));

    private Control BuildMobileLibraryGroupRow(LibraryGroup[] groups)
    {
        if (_mobileLibraryListMode)
        {
            var group = groups[0];
            var kind = _mobileLibraryTab switch { LibraryTab.Artists => "Artista", LibraryTab.Albums => "Álbum", _ => "Playlist" };
            return SearchCatalogRow(new("", group.Title, kind, group.Artwork?.Thumbnail),
                () => OpenMobileLibraryGroupAsync(group), group.Subtitle);
        }
        var row = new Grid { ColumnDefinitions = new(string.Join(",", Enumerable.Repeat("*", LibraryColumns))), ColumnSpacing = UiTheme.SectionGap,
            Margin = new Thickness(0, 0, 0, UiTheme.SectionGap) };
        for (var index = 0; index < groups.Length; index++)
        {
            var group = groups[index];
            var artwork = group.Artwork is { } track ? track with { Title = group.Title, Artist = "" }
                : new Track(group.Title, group.Title, "", "");
            var card = CoverCard(artwork, group.Subtitle, () => OpenMobileLibraryGroupAsync(group), songActions: false,
                cardSize: MobileLibraryCardSize, roundArtwork: _mobileLibraryTab == LibraryTab.Artists);
            Put(row, card, 0, index);
        }
        return row;
    }

    private Control BuildMobileLibraryHero(LibraryGroup group)
    {
        var hero = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 16 };
        var art = new Artwork(group.Artwork, UiTheme.RowHeight, highQuality: true);
        if (_mobileLibraryTab == LibraryTab.Artists) art.CornerRadius = new CornerRadius(UiTheme.RowHeight / 2d);
        hero.Children.Add(art);
        var labels = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        var kind = group.Playlist is not null ? "Playlist" : _mobileLibraryTab == LibraryTab.Albums ? "Álbum" : "Artista";
        labels.Children.Add(CollectionText(kind, UiTheme.Caption, Muted));
        var subtitle = CollectionText(group.Playlist is not null ? "Sua coleção" : "Faixas na sua biblioteca · " + group.Subtitle, UiTheme.Body, Muted);
        subtitle.TextWrapping = TextWrapping.Wrap;
        subtitle.MaxLines = 2;
        subtitle.TextTrimming = TextTrimming.CharacterEllipsis;
        labels.Children.Add(subtitle);
        if (group.Playlist is null && group.Tracks.FirstOrDefault() is { } track)
            labels.Children.Add(CollectionAction(_mobileLibraryTab == LibraryTab.Artists ? "Ver perfil online" : "Ver artista",
                () => OpenTrackArtistAsync(track)));
        Put(hero, labels, 0, 1);
        return UiTheme.Inner(hero);
    }

    private Task OpenMobileLibraryGroupAsync(LibraryGroup group)
    {
        EndTrackSelection();
        if (group.OnlinePlaylist is { } playlist && _account is { } account)
            return ShowAccountTracksAsync(account, playlist.Id, playlist.Title);
        RevealMobileTopBars();
        _mobileLibraryGroupOffset = _mobileLibraryScroll.Offset;
        _mobileLibraryGroup = group;
        _mobileLibraryScroll.Offset = default;
        RefreshMobileLibrary();
        return Task.CompletedTask;
    }

    private void CloseMobileLibraryGroup()
    {
        EndTrackSelection();
        RevealMobileTopBars();
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
            EndTrackSelection();
            _mobileLibrarySource = (LibrarySource)Math.Max(0, source.SelectedIndex);
            _mobileLibraryOrder = (LibraryOrder)Math.Max(0, order.SelectedIndex);
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
