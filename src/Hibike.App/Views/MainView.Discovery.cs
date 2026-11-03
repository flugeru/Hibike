using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Input.Platform;
using Avalonia.Input;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Hibike.Core.Models;
using Hibike.Core.Services;
using Track = Hibike.Core.Models.Track;

namespace Hibike.App.Views;

public sealed partial class MainView
{
    private Image _brandIcon = null!;
    private string? _libraryGroupsKey;
    private Popup? _songPopup;
    private void AttachSongHold(Control control, Track track)
    {
        control.SetValue(InputElement.IsHoldingEnabledProperty, true);
        var held = false;
        control.Holding += (_, e) =>
        {
            if (e.HoldingState != HoldingState.Started) return;
            held = true; e.Handled = true; ShowSongMenu(track, control);
        };
        control.AddHandler(PointerPressedEvent, (_, _) => held = false, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        control.AddHandler(PointerReleasedEvent, (_, e) => { if (held) { e.Handled = true; held = false; } }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        control.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Apps || e.Key == Key.F10 && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            { e.Handled = true; ShowSongMenu(track, control); }
        };
    }
    private void ShowSongMenu(Track track, Control anchor)
    {
        _songPopup?.SetCurrentValue(Popup.IsOpenProperty, false);
        var actions = (StackPanel)SongActions(track); actions.Spacing = 0;
        foreach (var button in actions.Children.OfType<Button>())
        {
            var label = button.Content as string ?? "";
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            var icon = label.Contains("favoritos") ? "heart" : label.Contains("artista") ? "search" : label.Contains("rádio") ? "shuffle" : label.Contains("Remover") ? "close" : "queue";
            row.Children.Add(Icons.Make(icon, UiTheme.Primary, 20)); row.Children.Add(Text(label, UiTheme.Caption)); button.Content = row;
            Avalonia.Automation.AutomationProperties.SetName(button, label);
            button.Classes.Add("subtle"); button.BorderThickness = new Thickness(0);
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Padding = new Thickness(16, 8); button.CornerRadius = new(0);
            button.Click += (_, _) => _songPopup?.SetCurrentValue(Popup.IsOpenProperty, false);
        }
        var popup = new Popup
        {
            PlacementTarget = anchor, Placement = PlacementMode.BottomEdgeAlignedRight,
            IsLightDismissEnabled = true,
            Child = new Border
            {
                Background = UiTheme.Brush(UiTheme.Surface), BorderBrush = UiTheme.Brush(UiTheme.Outline), BorderThickness = new Thickness(1), CornerRadius = new(8),
                Child = new ScrollViewer { Content = actions, Width = Math.Min(300, Math.Max(240, Bounds.Width - 32)), MaxHeight = Math.Max(180, Bounds.Height * .65) }
            }
        };
        _songPopup = popup; _root.Children.Add(popup);
        popup.KeyDown += (_, e) => { if (e.Key == Key.Escape) { popup.IsOpen = false; e.Handled = true; } };
        popup.Closed += (_, _) => { _root.Children.Remove(popup); if (_songPopup == popup) _songPopup = null; };
        popup.IsOpen = true;
    }
    private string _searchScope = "Online", _librarySection = "Músicas", _libraryOrder = "Recentes";
    private readonly WrapPanel _libraryGroups = new() { ItemSpacing = 18, LineSpacing = 20 };
    private readonly ScrollViewer _libraryGroupScroll = new() { IsVisible = false };
    private readonly StackPanel _sidebarPins = new() { Spacing = 4 };
    private string? _sidebarPinsKey;
    private readonly StackPanel _radioControls = new() { Spacing = 8 };
    private string? _radioControlsKey;
    private bool _relatedTab;
    private Task ShowAccountMenuAsync()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(ActionButton("Personalizar meu som", () => { CloseSheet(); ShowMusicOnboarding(edit: true); return Task.CompletedTask; }));
        panel.Children.Add(ActionButton("Configurações", () => { ShowSettings(); return Task.CompletedTask; }));
        ShowSheet("Preferências", panel); return Task.CompletedTask;
    }

    private Control SongActions(Track track)
    {
        var actions = new StackPanel { Spacing = 8 };
        actions.Children.Add(ActionButton("Não tenho interesse", () => { _music.DismissRecommendation(track); CloseSheet(); return Task.CompletedTask; }));
        actions.Children.Add(ActionButton("Tocar a seguir", () => { _music.PlayNext(track); CloseSheet(); return Task.CompletedTask; }));
        actions.Children.Add(ActionButton("Adicionar à fila", () => { _music.Enqueue(track); CloseSheet(); return Task.CompletedTask; }));
        if (!track.IsLocal) actions.Children.Add(ActionButton("Criar rádio desta música", async () => { CloseSheet(); await _music.StartRadioAsync(track); }));
        actions.Children.Add(ActionButton(_music.IsFavorite(track) ? "Remover dos favoritos" : "Salvar nos favoritos", async () => { await _music.ToggleFavoriteAsync(track); CloseSheet(); }));
        actions.Children.Add(ActionButton(_music.Pins.Any(t => t.Source == track.Source) ? "Desafixar de Ouvir de novo" : "Fixar em Ouvir de novo", () => { _music.TogglePin(track); CloseSheet(); return Task.CompletedTask; }));
        actions.Children.Add(ActionButton("Salvar na playlist local", () => { ShowPlaylistPicker(track); return Task.CompletedTask; }));
        if (_mobileCollections && _page == "library" && _mobileLibraryGroup?.Playlist is { } playlist && playlist.Tracks.Contains(track))
            actions.Children.Add(ActionButton("Remover desta playlist", () =>
            {
                _music.RemoveFromPlaylist(playlist, track);
                return Task.CompletedTask;
            }));
        actions.Children.Add(ActionButton("Buscar artista", async () => { CloseSheet(); _search.Text = track.Artist; _searchScope = "Online"; await SearchAsync(); }));
        if (!string.IsNullOrWhiteSpace(track.Album)) actions.Children.Add(ActionButton("Abrir álbum", () => { CloseSheet(); OpenLocalCollection(track.Album, _music.Favorites.Concat(_music.History).Where(t => t.Album == track.Album && t.Artist == track.Artist)); return Task.CompletedTask; }));
        if (!track.IsLocal && Uri.TryCreate(track.Source, UriKind.Absolute, out var uri) && uri.Scheme == "https")
            actions.Children.Add(ActionButton("Compartilhar: copiar link", async () => { var clipboard = TopLevel.GetTopLevel(this)?.Clipboard; if (clipboard is null) throw new InvalidOperationException("Área de transferência indisponível."); await clipboard.SetTextAsync(track.Source); CloseSheet(); }));
        if (_music.Queue.Contains(track))
            foreach (var (label, direction) in new[] { ("Mover para cima", -1), ("Mover para baixo", 1), ("Remover da fila", 0) })
                actions.Children.Add(ActionButton(label, () => { _music.EditQueue(track, direction); CloseSheet(); return Task.CompletedTask; }));
        return actions;
    }
    private void OpenLocalCollection(string title, IEnumerable<Track> tracks)
    {
        _collectionTracks = tracks.DistinctBy(t => t.Source).ToArray();
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(ActionButton("Tocar tudo", () => PlayCollectionAsync(_collectionTracks)));
        var list = new ItemsControl { ItemsSource = _collectionTracks, ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<Track>((track, _) => track is null ? new Border() : TrackRow(track)), ItemsPanel = new Avalonia.Controls.Templates.FuncTemplate<Panel?>(() => new VirtualizingStackPanel()) };
        // ponytail: collections open in bounded sheet; dedicated page only needed for richer album metadata.
        panel.Children.Add(new ScrollViewer { Content = list, MaxHeight = Math.Max(180, Bounds.Height * .5) });
        ShowSheet(title, panel);
    }
    private void ShowPlaylistPicker(Track track)
    {
        var panel = new StackPanel { Spacing = 8 };
        foreach (var playlist in _music.Playlists)
            panel.Children.Add(ActionButton(playlist.Name, () => { _music.AddToPlaylist(playlist, track); CloseSheet(); return Task.CompletedTask; }));
        panel.Children.Add(ActionButton("Nova playlist", () => { ShowNewPlaylist(track); return Task.CompletedTask; }));
        ShowSheet("Salvar na playlist local", panel);
    }
    private void ShowNewPlaylist(Track? track = null)
    {
        var panel = new StackPanel { Spacing = 12 }; var field = UiTheme.Field("Nome da playlist"); panel.Children.Add(field);
        panel.Children.Add(ActionButton("Criar", () => { _music.CreatePlaylist(field.Text ?? ""); if (track is not null) _music.AddToPlaylist(_music.Playlists.Last(), track); CloseSheet(); return Task.CompletedTask; }));
        ShowSheet("Nova playlist local", panel);
    }
    private void ShowLocalPlaylist(MusicController.LocalPlaylist playlist)
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(ActionButton("Tocar tudo", async () => { CloseSheet(); await PlayCollectionAsync(playlist.Tracks); _music.SelectionContext = "Playlist " + playlist.Name; _music.SetStatus("Playlist iniciada."); }));
        panel.Children.Add(ActionButton(playlist.Pinned ? "Desafixar da sidebar" : "Fixar na sidebar", () => { _music.PinPlaylist(playlist); ShowLocalPlaylist(playlist); return Task.CompletedTask; }));
        var name = UiTheme.Field("Nome"); name.Text = playlist.Name; panel.Children.Add(name);
        panel.Children.Add(ActionButton("Renomear", () => { _music.RenamePlaylist(playlist, name.Text ?? ""); ShowLocalPlaylist(playlist); return Task.CompletedTask; }));
        foreach (var track in playlist.Tracks)
        {
            var row = new Grid { ColumnDefinitions = new("*,Auto") };
            row.Children.Add(ActionButton(track.Title, () => { CloseSheet(); return _music.PlayAsync(track); }));
            Put(row, IconButton("close", "Remover " + track.Title + " da playlist", () => { _music.RemoveFromPlaylist(playlist, track); ShowLocalPlaylist(playlist); return Task.CompletedTask; }), 0, 1); panel.Children.Add(row);
        }
        panel.Children.Add(ActionButton("Excluir playlist…", () =>
        {
            var confirm = new StackPanel { Spacing = 12 }; confirm.Children.Add(Text("Excluir playlist local? Arquivos de áudio não serão apagados.", UiTheme.Caption));
            confirm.Children.Add(ActionButton("Confirmar exclusão", () => { _music.DeletePlaylist(playlist); CloseSheet(); return Task.CompletedTask; })); ShowSheet("Excluir " + playlist.Name, confirm); return Task.CompletedTask;
        }));
        ShowSheet(playlist.Name + " · Local", new ScrollViewer { Content = panel, MaxHeight = Math.Max(220, Bounds.Height * .65) });
    }
    private CheckBox CreateRadioToggle()
    {
        var toggle = new CheckBox { Content = "Reprodução automática", IsChecked = _music.Autoplay, MinHeight = 48 };
        toggle.IsCheckedChanged += (_, _) => { CloseSheet(); _music.SetAutoplay(toggle.IsChecked == true); };
        return toggle;
    }
    private WrapPanel CreateRadioTabs()
    {
        var tabs = new WrapPanel { ItemSpacing = 8 };
        foreach (var related in new[] { false, true })
        {
            var button = ActionButton(related ? "Relacionadas" : "Fila", () =>
            { CloseSheet(); _relatedTab = related; _queueFingerprint = null; Refresh(); return Task.CompletedTask; });
            if (_relatedTab == related) button.Classes.Add("primary");
            tabs.Children.Add(button);
        }
        return tabs;
    }
    private WrapPanel CreateRadioModes()
    {
        var modes = new WrapPanel { ItemSpacing = 6 };
        foreach (var mode in new[] { "Tudo", "Familiar", "Descobrir" })
        {
            var button = ActionButton(mode, () => { CloseSheet(); _music.SetDiscovery(mode); return Task.CompletedTask; });
            if (_music.DiscoveryMode == mode) button.Classes.Add("primary");
            modes.Children.Add(button);
        }
        return modes;
    }
    private Button CreateRadioRetryButton()
    {
        var button = ActionButton(_music.RadioLoading ? "Buscando sugestões…" : "Buscar sugestões novamente", async () =>
        { CloseSheet(); await _music.RetryRadioAsync(); });
        button.IsEnabled = _music.Current is not null && !_music.RadioLoading && !_music.LoadingAudio;
        return button;
    }
    private void ShowRadioOptions()
    {
        // Every opening owns fresh controls; reusing sheet children breaks reopening on mobile.
        var options = new StackPanel { Spacing = 8 };
        options.Children.Add(CreateRadioToggle()); options.Children.Add(CreateRadioTabs()); options.Children.Add(CreateRadioModes());
        var state = Text("", UiTheme.Caption, Muted); state.TextWrapping = TextWrapping.Wrap;
        var retry = CreateRadioRetryButton(); options.Children.Add(state); options.Children.Add(retry);
        void UpdateState()
        {
            state.Text = _music.RadioStatus;
            retry.Content = _music.RadioLoading ? "Buscando sugestões…" : "Buscar sugestões novamente";
            retry.IsEnabled = _music.Current is not null && !_music.RadioLoading && !_music.LoadingAudio;
        }
        options.AttachedToVisualTree += (_, _) => { _music.Changed += UpdateState; UpdateState(); };
        options.DetachedFromVisualTree += (_, _) => _music.Changed -= UpdateState;
        UpdateState(); ShowSheet("Rádio", options);
    }
    private void RefreshDiscovery()
    {
        var pinsKey = string.Join("|", _music.Playlists.Where(p => p.Pinned).Select(p => p.Name));
        if (_sidebarPinsKey != pinsKey)
        {
            _sidebarPinsKey = pinsKey; _sidebarPins.Children.Clear();
            foreach (var playlist in _music.Playlists.Where(p => p.Pinned))
                _sidebarPins.Children.Add(ActionButton(playlist.Name, () => { ShowLocalPlaylist(playlist); return Task.CompletedTask; }));
        }
        var key = _music.Autoplay + ":" + _music.RadioLoading + ":" + _music.LoadingAudio + ":" + _music.Current?.Source + ":" + _music.RadioStatus + ":" + _music.DiscoveryMode + ":" + _music.PlaybackContext + ":" + _relatedTab + ":" + (Bounds.Width < UiTheme.DesktopWidth);
        if (_radioControlsKey != key)
        {
            _radioControlsKey = key; _radioControls.Children.Clear();
            if (Bounds.Width < UiTheme.DesktopWidth)
                _radioControls.Children.Add(ActionButton(_music.RadioLoading ? "Buscando sugestões…" : "Rádio · " + (_music.Autoplay ? "Ligado" : "Desligado") + " · " + _music.DiscoveryMode,
                    () => { ShowRadioOptions(); return Task.CompletedTask; }));
            else
            {
                _radioControls.Children.Add(CreateRadioToggle());
                _radioControls.Children.Add(CreateRadioTabs());
                _radioControls.Children.Add(CreateRadioModes());
                var state = Text(_music.RadioStatus, UiTheme.Caption, Muted); state.TextWrapping = TextWrapping.Wrap;
                _radioControls.Children.Add(state);
                if (_music.RadioLoading) _radioControls.Children.Add(LoadingRing(22, UiTheme.Primary));
                _radioControls.Children.Add(CreateRadioRetryButton());
            }
        }
        _libraryGroups.IsVisible = !_mobileCollections && _page == "library" && _librarySection != "Músicas";
        _libraryGroupScroll.IsVisible = _libraryGroups.IsVisible;
        if (!_libraryGroups.IsVisible) return;
        _trackScroll.IsVisible = _listHeading.IsVisible = _emptyState.IsVisible = false;
        var query = _libraryFilter.Text?.Trim() ?? "";
        var groupsKey = _librarySection + ":" + _libraryOrder + ":" + _libraryKind + ":" + query + ":" + (int)_main.Bounds.Width
            + ":" + string.Join("|", _music.Favorites.Select(t => t.ToString())) + ":" + string.Join("|", _music.Playlists.Select(p => p.Name + string.Join(",", p.Tracks)))
            + ":" + string.Join("|", _accountPlaylists.Select(p => p.Id + p.Title));
        if (_libraryGroupsKey == groupsKey) return;
        _libraryGroupsKey = groupsKey;
        _libraryGroups.Children.Clear();
        var entries = new List<(Track Art, string Subtitle, Func<Task> Action)>();
        bool Matches(string text) => text.Contains(query, StringComparison.CurrentCultureIgnoreCase);
        if (_librarySection == "Playlists")
        {
            if (_libraryKind != "Online")
            foreach (var playlist in _music.Playlists.Where(p => Matches(p.Name)))
                entries.Add((new Track("playlist:" + playlist.Name, playlist.Name, "Playlist", "playlist:" + playlist.Name, Thumbnail: playlist.Tracks.FirstOrDefault()?.Thumbnail), $"Playlist · {playlist.Tracks.Count} faixas · Local", () => { ShowLocalPlaylist(playlist); return Task.CompletedTask; }));
            if (_libraryKind != "Locais")
            foreach (var playlist in _accountPlaylists.Where(p => Matches(p.Title)))
                entries.Add((new Track(playlist.Id, playlist.Title, "Playlist", "https://www.youtube.com/playlist?list=" + Uri.EscapeDataString(playlist.Id), Thumbnail: playlist.Thumbnail), "Playlist · YouTube", () => ShowAccountTracksAsync(_account!, playlist.Id, playlist.Title)));
        }
        else
        {
            var groups = _music.Favorites.Where(t => (_libraryKind != "Locais" || t.IsLocal) && (_libraryKind != "Online" || !t.IsLocal))
                .Where(t => _librarySection == "Artistas" || !string.IsNullOrWhiteSpace(t.Album))
                .GroupBy(t => _librarySection == "Artistas" ? t.Artist : t.Artist + " · " + t.Album);
            foreach (var group in groups.Where(g => Matches(g.Key)))
                entries.Add((group.First() with { Title = _librarySection == "Artistas" ? group.First().Artist : group.First().Album! }, $"{(_librarySection == "Artistas" ? "Artista" : "Álbum")} · {group.Count()} faixas", () => { OpenLocalCollection(group.Key, group); return Task.CompletedTask; }));
        }
        IEnumerable<(Track Art, string Subtitle, Func<Task> Action)> ordered = entries;
        if (_libraryOrder == "Título A–Z") ordered = entries.OrderBy(e => e.Art.Title, StringComparer.CurrentCultureIgnoreCase);
        else if (_libraryOrder == "Artista") ordered = entries.OrderBy(e => e.Art.Artist, StringComparer.CurrentCultureIgnoreCase);
        else if (_librarySection != "Playlists") ordered = entries.OrderByDescending(e => _music.Listening.GetValueOrDefault(MusicController.SongKey(e.Art))?.LastPlayed ?? DateTime.MinValue);
        foreach (var entry in ordered)
            _libraryGroups.Children.Add(CoverCard(entry.Art, entry.Subtitle, entry.Action, playlist: _librarySection == "Playlists", songActions: false,
                cardSize: Bounds.Width < UiTheme.DesktopWidth ? Math.Max(120, (_main.Bounds.Width - 18) / 2) : 196));
        if (entries.Count == 0) _libraryGroups.Children.Add(Text("Nenhuma coleção encontrada para estes filtros.", UiTheme.Caption, Muted));
    }
}
