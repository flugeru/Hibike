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
    private Popup? _songPopup;
    private void AttachSongHold(Control control, Track track, Action<Control>? openMenu = null)
    {
        openMenu ??= anchor => ShowSongMenu(track, anchor);
        control.SetValue(InputElement.IsHoldingEnabledProperty, true);
        var held = false;
        control.Holding += (_, e) =>
        {
            if (e.HoldingState != HoldingState.Started) return;
            held = true; e.Handled = true; openMenu(control);
        };
        control.AddHandler(PointerPressedEvent, (_, _) => held = false, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        control.AddHandler(PointerReleasedEvent, (_, e) => { if (held) { e.Handled = true; held = false; } }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        control.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Apps || e.Key == Key.F10 && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            { e.Handled = true; openMenu(control); }
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
            var icon = label.Contains("Baixar") ? "download" : label.Contains("favoritos") ? "heart" : label.Contains("artista") ? "search" : label.Contains("rádio") ? "shuffle" : label.Contains("Remover") ? "close" : "queue";
            row.Children.Add(Icons.Make(icon, UiTheme.Primary, 20)); row.Children.Add(Text(label, UiTheme.Caption)); button.Content = row;
            Avalonia.Automation.AutomationProperties.SetName(button, label);
            button.Classes.Add("subtle"); button.BorderThickness = new Thickness(0);
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Padding = new Thickness(16, 8); button.CornerRadius = new(0);
            button.Click += (_, _) => { _songPopup?.SetCurrentValue(Popup.IsOpenProperty, false); ClosePlayer(); };
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
        if (popup.Child is Control menu) Animate(menu, -4);
    }
    private string _searchScope = "Online";
    private readonly StackPanel _sidebarPins = new() { Spacing = 4 };
    private string? _sidebarPinsKey;
    private bool _relatedTab;
    private Task ShowAccountMenuAsync()
    {
        ToggleTopMenu(); return Task.CompletedTask;
    }

    private Control SongActions(Track track)
    {
        var actions = new StackPanel { Spacing = 8 };
        actions.Children.Add(ActionButton("Não tenho interesse", () => { _music.DismissRecommendation(track); CloseSheet(); return Task.CompletedTask; }));
        actions.Children.Add(ActionButton("Tocar a seguir", () => { _music.PlayNext(track); CloseSheet(); return Task.CompletedTask; }));
        actions.Children.Add(ActionButton("Adicionar à fila", () => { _music.Enqueue(track); CloseSheet(); return Task.CompletedTask; }));
        if (!track.IsLocal && !track.IsArtist && _music.CanUseOnline)
        {
            actions.Children.Add(ActionButton("Baixar para ouvir offline", () => DownloadTrackAsync(track)));
            actions.Children.Add(ActionButton("Criar rádio desta música", async () => { CloseSheet(); await _music.StartRadioAsync(track); }));
        }
        actions.Children.Add(ActionButton(_music.IsFavorite(track) ? "Remover dos favoritos" : "Salvar nos favoritos", async () => { await _music.ToggleFavoriteAsync(track); CloseSheet(); }));
        actions.Children.Add(ActionButton(_music.Pins.Any(t => t.Source == track.Source) ? "Desafixar de Ouvir de novo" : "Fixar em Ouvir de novo", () => { _music.TogglePin(track); CloseSheet(); return Task.CompletedTask; }));
        actions.Children.Add(ActionButton("Salvar na playlist local", () => { ShowPlaylistPicker(track); return Task.CompletedTask; }));
        if (_page == "library" && _mobileLibraryGroup?.Playlist is { } playlist && playlist.Tracks.Contains(track))
            actions.Children.Add(ActionButton("Remover desta playlist", () =>
            {
                _music.RemoveFromPlaylist(playlist, track);
                return Task.CompletedTask;
            }));
        if (_music.CanUseOnline) actions.Children.Add(ActionButton("Ver artista", () => OpenTrackArtistAsync(track)));
        if (!string.IsNullOrWhiteSpace(track.Album)) actions.Children.Add(ActionButton("Abrir álbum", () => { CloseSheet(); OpenLocalCollection(track.Album, _music.Favorites.Concat(_music.History).Append(track).Where(t => t.Album == track.Album && t.Artist == track.Artist)); return Task.CompletedTask; }));
        if (!track.IsLocal && Uri.TryCreate(track.Source, UriKind.Absolute, out var uri) && uri.Scheme == "https")
            actions.Children.Add(ActionButton("Compartilhar: copiar link", async () => { var clipboard = TopLevel.GetTopLevel(this)?.Clipboard; if (clipboard is null) throw new InvalidOperationException("Área de transferência indisponível."); await clipboard.SetTextAsync(track.Source); CloseSheet(); }));
        if (_music.Queue.Contains(track))
            foreach (var (label, direction) in new[] { ("Mover para cima", -1), ("Mover para baixo", 1), ("Remover da fila", 0) })
                actions.Children.Add(ActionButton(label, () => { _music.EditQueue(track, direction); CloseSheet(); return Task.CompletedTask; }));
        return actions;
    }
    private void OpenLocalCollection(string title, IEnumerable<Track> tracks)
    {
        OpenCollection(title, tracks.DistinctBy(t => t.Source).ToArray(), "Álbum");
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
        _page = "library";
        Refresh();
        _mobileLibraryTab = LibraryTab.Playlists;
        _ = OpenMobileLibraryGroupAsync(new LibraryGroup(playlist.Name, TrackCount(playlist.Tracks.Count), playlist.Tracks.FirstOrDefault(), playlist.Tracks, playlist));
    }
    private void ShowLegacyPlaylistEditor(MusicController.LocalPlaylist playlist)
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
        ShowSheet(playlist.Name + " · Local", panel);
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
            { CloseSheet(); _relatedTab = related; Refresh(); return Task.CompletedTask; });
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
    private void RefreshDiscovery()
    {
        var pinsKey = string.Join("|", _music.Playlists.Where(p => p.Pinned).Select(p => p.Name));
        if (_sidebarPinsKey != pinsKey)
        {
            _sidebarPinsKey = pinsKey; _sidebarPins.Children.Clear();
            foreach (var playlist in _music.Playlists.Where(p => p.Pinned))
                _sidebarPins.Children.Add(ActionButton(playlist.Name, () => { ShowLocalPlaylist(playlist); return Task.CompletedTask; }));
        }
    }
}
