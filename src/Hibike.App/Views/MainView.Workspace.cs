using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Hibike.Core.Models;
using Track = Hibike.Core.Models.Track;

namespace Hibike.App.Views;

public sealed partial class MainView
{
    private readonly Border _queuePane = new();
    private readonly Grid _queueLayout = new() { RowDefinitions = new("Auto,Auto,Auto,*"), RowSpacing = 16 };
    private readonly ContentControl _queueCurrent = new();
    private readonly ItemsControl _upNext = new() { ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()) };
    private readonly TextBlock _queueSummary = Text("", UiTheme.Caption, Muted);
    private readonly Border _collectionPage = new();
    private readonly Border _desktopDock = new();
    private readonly ContentControl _dockArtwork = new();
    private readonly TextBlock _dockTitle = Text("", UiTheme.Body);
    private readonly TextBlock _dockArtist = Text("", UiTheme.Caption, Muted);
    private readonly Slider _dockSeek = new() { Minimum = 0, Maximum = 1, MinHeight = 48 };
    private readonly TextBlock _dockTime = Text("", UiTheme.Caption, Muted);
    private Button _dockPlay = null!;
    private bool _dockSeeking;
    private bool _queueOpen;
    private string? _queueFingerprint;
    private Track? _dockTrack;
    private IReadOnlyList<Track> _collectionTracks = [];

    private void InitializeWorkspace(Grid content)
    {
        _root.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(0)));
        var queueHeader = new Grid { ColumnDefinitions = new("*,Auto") };
        queueHeader.Children.Add(Text("Fila de reprodução", UiTheme.DialogTitle));
        Put(queueHeader, IconButton("close", "Fechar fila", () => { _queueOpen = false; if (_page == "queue") _page = "home"; Refresh(); return Task.CompletedTask; }), 0, 1);
        _queueLayout.Children.Add(queueHeader);
        Put(_queueLayout, _queueCurrent, 1, 0);
        var actions = new StackPanel { Spacing = 8 };
        actions.Children.Add(_queueSummary);
        var buttons = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };
        buttons.Children.Add(ActionButton("Embaralhar", () => { _music.ReorderQueue(true); return Task.CompletedTask; }));
        buttons.Children.Add(ActionButton("Limpar próximas", () => { _music.ClearQueue(); return Task.CompletedTask; }));
        actions.Children.Add(buttons); Put(_queueLayout, actions, 2, 0);
        _upNext.ItemTemplate = new FuncDataTemplate<Track>((track, _) => track is null ? new Border() : QueueRow(track));
        Put(_queueLayout, new ScrollViewer { Content = _upNext, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden }, 3, 0);
        _queuePane.Child = _queueLayout; _queuePane.Padding = new Thickness(20); _queuePane.Background = UiTheme.Brush(UiTheme.Surface);
        _queuePane.BorderBrush = UiTheme.Brush(UiTheme.Outline); _queuePane.BorderThickness = new Thickness(1, 0, 0, 0);
        Put(_root, _queuePane, 0, 2);
        Put(content, _collectionPage, 5, 0);
        BuildDesktopDock(); Put(_root, _desktopDock, 1, 0); Grid.SetColumnSpan(_desktopDock, 3);
    }

    private void ResizeWorkspace()
    {
        var desktop = Bounds.Width >= 1100;
        var destination = desktop ? _root : (Grid)_main.Child!;
        if (!ReferenceEquals(_queuePane.Parent, destination))
        {
            if (_queuePane.Parent is Panel parent) parent.Children.Remove(_queuePane);
            Put(destination, _queuePane, desktop ? 0 : 5, desktop ? 2 : 0);
        }
        if (desktop)
        {
            _root.ColumnDefinitions[2].Width = new GridLength(_queueOpen || _page == "queue" ? 350 : 0);
        }
        else
        {
            _root.ColumnDefinitions[2].Width = new GridLength(0);
        }
        _queuePane.IsVisible = !_showingPlayer && (desktop ? _queueOpen || _page == "queue" : _page == "queue");
        var display = _music.Pending ?? _music.Current;
        _desktopDock.IsVisible = display is not null && !_showingPlayer && Bounds.Width >= 1000;
        _mini.IsVisible = display is not null && !_showingPlayer && !_desktopDock.IsVisible;
        if (_showingPlayer) _queuePane.IsVisible = false;
        if (_overlay is not null) Grid.SetColumnSpan(_overlay, 3);
    }

    private void RefreshWorkspace()
    {
        ResizeWorkspace();
        var queue = _page == "queue";
        var collection = _page == "collection";
        if (queue || collection) _trackScroll.IsVisible = _listHeading.IsVisible = _listTools.IsVisible = false;
        _collectionPage.IsVisible = collection;
        _pageTitle.Text = collection ? "Coleção" : queue ? "Fila de reprodução" : _pageTitle.Text;
        var fingerprint = string.Join("|", _music.Queue.Select(t => t.Id + ":" + t.Source)) + ":" + _music.Current?.Id;
        if (_queueFingerprint != fingerprint)
        {
            _queueFingerprint = fingerprint;
            var index = _music.Current is null ? -1 : _music.Queue.IndexOf(_music.Current);
            var upcoming = _music.Queue.Skip(index + 1).ToArray();
            _upNext.ItemsSource = upcoming;
            var duration = upcoming.Sum(t => Math.Max(0, t.DurationSeconds));
            _queueSummary.Text = upcoming.Length == 0 ? "Nenhuma faixa a seguir. Adicione músicas pelo menu." : $"A SEGUIR · {upcoming.Length} {(upcoming.Length == 1 ? "faixa" : "faixas")} · {Time(duration)}";
            var current = new StackPanel { Spacing = 12 };
            current.Children.Add(Text("TOCANDO AGORA", UiTheme.Label, Muted));
            if (_music.Current is { } track)
            {
                var row = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 12 };
                row.Children.Add(new Artwork(track, 64));
                var labels = new StackPanel { Spacing = 4 };
                var title = Text(TrackTextFormatter.Title(track.Title, track.Artist), UiTheme.Body); title.TextWrapping = TextWrapping.Wrap; title.MaxLines = 2; labels.Children.Add(title);
                var artist = Text(track.Artist, UiTheme.Caption, Muted); artist.TextTrimming = TextTrimming.CharacterEllipsis; labels.Children.Add(artist);
                Put(row, labels, 0, 1); current.Children.Add(row);
            }
            else current.Children.Add(Text("Escolha uma música para começar.", UiTheme.Caption, Muted));
            _queueCurrent.Content = UiTheme.Card(current, 12);
        }
        RefreshDock();
    }

    private Control QueueRow(Track track)
    {
        var grid = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 4, Margin = new Thickness(0, 4) };
        var play = ActionButton("", () => _music.PlayAsync(track, _music.Queue.IndexOf(track))); play.Classes.Add("subtle"); play.Padding = new Thickness(0, 6); play.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        var info = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 10 };
        info.Children.Add(new Artwork(track, 44));
        var labels = new StackPanel { Spacing = 3 };
        var title = Text(TrackTextFormatter.Title(track.Title, track.Artist), UiTheme.Body); title.TextTrimming = TextTrimming.CharacterEllipsis; labels.Children.Add(title);
        var artist = Text(track.Artist, UiTheme.Caption, Muted); artist.TextTrimming = TextTrimming.CharacterEllipsis; labels.Children.Add(artist);
        Put(info, labels, 0, 1); play.Content = info; grid.Children.Add(play);
        Avalonia.Automation.AutomationProperties.SetName(play, "Reproduzir da fila: " + track.Title);
        Put(grid, IconButton("more", "Organizar " + track.Title, () =>
        {
            var options = new StackPanel { Spacing = 8 };
            foreach (var (label, direction) in new[] { ("Mover para cima", -1), ("Mover para baixo", 1), ("Remover da fila", 0) })
                options.Children.Add(ActionButton(label, () => { _music.EditQueue(track, direction); CloseSheet(); return Task.CompletedTask; }));
            ShowSheet(track.Title, options); return Task.CompletedTask;
        }), 0, 1);
        return grid;
    }

    private void BuildDesktopDock()
    {
        var grid = new Grid { ColumnDefinitions = new("*,2*,*"), ColumnSpacing = 24 };
        var metadata = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 12 };
        metadata.Children.Add(_dockArtwork);
        var labels = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        _dockTitle.TextTrimming = _dockArtist.TextTrimming = TextTrimming.CharacterEllipsis; labels.Children.Add(_dockTitle); labels.Children.Add(_dockArtist); Put(metadata, labels, 0, 1);
        var open = ActionButton("", () => { ShowPlayer(); return Task.CompletedTask; }); open.Content = metadata; open.Classes.Add("subtle"); open.Padding = new Thickness(0); open.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Avalonia.Automation.AutomationProperties.SetName(open, "Expandir reprodução"); grid.Children.Add(open);
        var center = new Grid { RowDefinitions = new("Auto,Auto") };
        var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 12 };
        controls.Children.Add(IconButton("previous", "Faixa anterior", _music.PreviousAsync));
        _dockPlay = IconButton("play", "Reproduzir ou pausar", _music.ToggleAsync, UiTheme.OnPrimary); _dockPlay.Classes.Add("primary"); controls.Children.Add(_dockPlay);
        controls.Children.Add(IconButton("next", "Próxima faixa da fila", _music.NextAsync)); center.Children.Add(controls);
        _dockSeek.AddHandler(PointerPressedEvent, (_, _) => _dockSeeking = true, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _dockSeek.AddHandler(PointerReleasedEvent, (_, _) => { _dockSeeking = false; _music.Player.Seek(_dockSeek.Value); }, Avalonia.Interactivity.RoutingStrategies.Bubble, true);
        _dockSeek.KeyUp += (_, _) => _music.Player.Seek(_dockSeek.Value);
        Avalonia.Automation.AutomationProperties.SetName(_dockSeek, "Posição da reprodução");
        Put(center, _dockSeek, 1, 0); Put(grid, center, 0, 1);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(_dockTime);
        right.Children.Add(IconButton("queue", "Mostrar ou ocultar fila", () => { _queueOpen = !_queueOpen; Refresh(); return Task.CompletedTask; }));
        var volume = new Slider { Minimum = 0, Maximum = 1, Value = _music.Player.Volume, Width = 88, MinHeight = 48 };
        Avalonia.Automation.AutomationProperties.SetName(volume, "Volume da barra"); volume.ValueChanged += (_, _) => _music.Player.Volume = volume.Value;
        right.Children.Add(volume); Put(grid, right, 0, 2);
        _desktopDock.Child = grid; _desktopDock.Padding = new Thickness(24, 10); _desktopDock.Background = UiTheme.Brush(UiTheme.Surface);
        _desktopDock.BorderBrush = UiTheme.Brush(UiTheme.Outline); _desktopDock.BorderThickness = UiTheme.TopBorder;
    }

    private void RefreshDock()
    {
        if (_dockPlay is null) return;
        var track = _music.Pending ?? _music.Current;
        if (!Equals(track, _dockTrack)) { _dockTrack = track; _dockArtwork.Content = new Artwork(track, 56); }
        _dockTitle.Text = TrackTextFormatter.Title(track?.Title, track?.Artist); _dockArtist.Text = track?.Artist;
        _dockPlay.Content = Icons.Make(_music.Player.IsPlaying ? "pause" : "play", UiTheme.OnPrimary);
        _dockSeek.Maximum = Math.Max(1, _music.Player.Duration);
        if (!_dockSeeking) _dockSeek.Value = Math.Clamp(_music.Position, 0, _dockSeek.Maximum);
        _dockTime.Text = Time(_music.Position) + " / " + Time(_music.Player.Duration);
    }

    private void OpenCollection(string title, IReadOnlyList<Track> tracks)
    {
        _collectionTracks = tracks;
        var grid = new Grid { RowDefinitions = new("Auto,Auto,*"), RowSpacing = 20 };
        var hero = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 20 };
        hero.Children.Add(new Artwork(tracks.FirstOrDefault(), 100));
        var labels = new StackPanel { Spacing = 8 };
        labels.Children.Add(Text("PLAYLIST", UiTheme.Label, Muted));
        var heading = Text(title, UiTheme.PageTitle); heading.TextWrapping = TextWrapping.Wrap; heading.MaxLines = 3; labels.Children.Add(heading);
        labels.Children.Add(Text($"{tracks.Count} faixas · {Time(tracks.Sum(t => Math.Max(0, t.DurationSeconds)))}", UiTheme.Caption, Muted)); Put(hero, labels, 0, 1); grid.Children.Add(hero);
        var actions = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };
        var play = ActionButton("Reproduzir playlist", () => PlayCollectionAsync(_collectionTracks)); play.Classes.Add("primary"); actions.Children.Add(play);
        actions.Children.Add(ActionButton("Adicionar à fila", () => { foreach (var track in _collectionTracks) _music.Queue.Add(track); _music.SetStatus("Playlist adicionada à fila."); return Task.CompletedTask; }));
        actions.Children.Add(ActionButton("Voltar ao início", () => { _page = "home"; Refresh(); return Task.CompletedTask; })); Put(grid, actions, 1, 0);
        var list = new ItemsControl { ItemsSource = tracks, ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()), ItemTemplate = new FuncDataTemplate<Track>((track, _) => track is null ? new Border() : TrackRow(track)) };
        Put(grid, new ScrollViewer { Content = list, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden }, 2, 0);
        _collectionPage.Child = grid; _page = "collection"; CloseSheet(); Refresh();
    }
}
