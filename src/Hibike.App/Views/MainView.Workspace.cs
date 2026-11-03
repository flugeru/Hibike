using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Hibike.Core.Models;
using Hibike.Core.Services;
using Track = Hibike.Core.Models.Track;

namespace Hibike.App.Views;

public sealed partial class MainView
{
    private readonly Border _queuePane = new();
    private readonly Grid _queueLayout = new() { RowDefinitions = new("Auto,Auto,Auto,*"), RowSpacing = 16 };
    private readonly ContentControl _queueCurrent = new();
    private readonly ItemsControl _upNext = new() { ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()) };
    private readonly TextBlock _queueSummary = Text("", UiTheme.Caption, Muted);
    private readonly Grid _queueMobileActions = new() { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = 4 };
    private Grid _queueButtons = null!;
    private TextBlock _queueTitle = null!;
    private Button _queueClose = null!;
    private readonly Border _collectionPage = new();
    private readonly Border _desktopDock = new();
    private readonly ContentControl _dockArtwork = new();
    private readonly TextBlock _dockTitle = Text("", UiTheme.Body);
    private readonly TextBlock _dockArtist = Text("", UiTheme.Caption, Muted);
    private readonly Slider _dockSeek = new() { Minimum = 0, Maximum = 1, MinHeight = 48 };
    private readonly TextBlock _dockTime = Text("", UiTheme.Caption, Muted);
    private readonly TextBlock _dockDuration = Text("", UiTheme.Caption, Muted);
    private readonly TextBlock _dockQuality = Text("", UiTheme.Caption, Muted);
    private Button _sourceButton = null!;
    private async Task ShowPlaybackSourcesAsync()
    {
        if (_music.Current is not { } original) return;
        var panel = new StackPanel { Spacing = 12 };
        var warning = Text(PlaybackSources.Warning + " Trocar fonte reinicia faixa; introduções podem diferir.", UiTheme.Caption, Muted);
        warning.TextWrapping = TextWrapping.Wrap; panel.Children.Add(warning);
        void AddSource(Track track, bool alternative)
        {
            panel.Children.Add(ActionButton(PlaybackSources.Provider(track) + (alternative ? " · Correspondência provável" : " · Original"), async () =>
            { CloseSheet(); await _music.SwitchPlaybackSourceAsync(track); }));
        }
        AddSource(original, false);
        var status = Text("Buscando fontes compatíveis…", UiTheme.Caption, Muted); panel.Children.Add(status);
        ShowSheet("Fonte de reprodução", panel);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            var sources = await _music.FindPlaybackSourcesAsync(timeout.Token);
            if (_music.Current != original) return;
            foreach (var source in sources) AddSource(source, true);
            status.Text = sources.Count == 0 ? "Nenhuma alternativa com metadados compatíveis." : "YouTube é fonte principal. Identidade por áudio não verificada.";
        }
        catch (Exception) { status.Text = "Fontes alternativas indisponíveis; original preservada."; }
    }
    private Border _dockQualityBadge = null!;
    private TextBlock? _expandedQuality;
    private Button _dockPlay = null!;
    private Button _dockShuffle = null!, _dockRepeat = null!, _dockFavorite = null!;
    private bool _dockSeeking;
    private bool _queueOpen;
    private string? _queueFingerprint;
    private Track? _dockTrack;
    private Button? _detailShuffle, _detailRepeat, _detailFavorite;
    private Slider? _dockVolume, _detailVolume;
    private IReadOnlyList<Track> _collectionTracks = [];

    private void InitializeWorkspace(Grid content)
    {
        _root.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(0)));
        var queueHeader = new Grid { ColumnDefinitions = new("*,Auto") };
        var queueTitle = Text("Fila de reprodução", UiTheme.DialogTitle); queueTitle.TextTrimming = TextTrimming.CharacterEllipsis;
        _queueTitle = queueTitle;
        queueHeader.Children.Add(queueTitle);
        _queueClose = IconButton("close", "Fechar fila", () => { _queueOpen = false; if (_page == "queue") _page = "home"; Refresh(); return Task.CompletedTask; });
        Put(queueHeader, _queueClose, 0, 1);
        _queueLayout.Children.Add(queueHeader);
        Put(_queueLayout, _queueCurrent, 1, 0);
        var actions = new StackPanel { Spacing = 8 };
        _queueSummary.TextWrapping = TextWrapping.Wrap;
        actions.Children.Add(_queueSummary);
        actions.Children.Add(_radioControls);
        var buttons = new Grid { ColumnDefinitions = new("*,*"), ColumnSpacing = 8 };
        _queueButtons = buttons;
        buttons.Children.Add(ActionButton("Embaralhar", () => { _music.ReorderQueue(true); return Task.CompletedTask; }));
        Put(buttons, ActionButton("Limpar próximas", () => { _music.ClearQueue(); return Task.CompletedTask; }), 0, 1);
        actions.Children.Add(buttons); Put(_queueLayout, actions, 2, 0);
        var mobileHeading = Text("Próximas músicas", UiTheme.SectionTitle);
        _queueMobileActions.Children.Add(mobileHeading);
        var shuffle = IconButton("shuffle", "Embaralhar próximas faixas", () => { _music.ReorderQueue(true); return Task.CompletedTask; }); shuffle.Classes.Add("subtle");
        var clear = IconButton("more", "Ações da fila", () =>
        {
            var options = new StackPanel { Spacing = 8 };
            options.Children.Add(ActionButton("Limpar próximas faixas", () => { _music.ClearQueue(); CloseSheet(); return Task.CompletedTask; }));
            ShowSheet("Fila", options); return Task.CompletedTask;
        }); clear.Classes.Add("subtle");
        Put(_queueMobileActions, shuffle, 0, 1); Put(_queueMobileActions, clear, 0, 2);
        actions.Children.Insert(0, _queueMobileActions);
        _upNext.ItemTemplate = new FuncDataTemplate<Track>((track, _) => track is null ? new Border() : QueueRow(track));
        Put(_queueLayout, UiTheme.FadingScroll(_upNext), 3, 0);
        _queuePane.Child = _queueLayout; _queuePane.Padding = new Thickness(20); _queuePane.Background = UiTheme.Brush(UiTheme.Surface);
        _queuePane.BorderBrush = UiTheme.Brush(UiTheme.Outline); _queuePane.BorderThickness = new Thickness(1, 0, 0, 0);
        Put(_root, _queuePane, 0, 2);
        Put(content, _collectionPage, 5, 0);
        BuildDesktopDock(); Put(_root, _desktopDock, 1, 0); Grid.SetColumnSpan(_desktopDock, 3);
    }

    private void ResizeWorkspace()
    {
        var desktop = !_mobileCollections && Bounds.Width >= 1100;
        var mobile = Bounds.Width < UiTheme.DesktopWidth;
        _queuePane.Padding = mobile ? UiTheme.Zero : new Thickness(20);
        ((Control)_queueTitle.Parent!).IsVisible = !mobile;
        _queuePane.Background = UiTheme.Brush(mobile ? UiTheme.Background : UiTheme.Surface);
        _queuePane.BorderThickness = mobile ? UiTheme.Zero : new Thickness(1, 0, 0, 0);
        _queueCurrent.IsVisible = true;
        _queueClose.IsVisible = !mobile;
        _queueButtons.IsVisible = !mobile;
        _queueMobileActions.IsVisible = mobile;
        _queueTitle.Text = mobile ? "Sua fila" : "Fila de reprodução";
        _queueTitle.FontSize = mobile ? UiTheme.PageTitle : UiTheme.DialogTitle;
        _queueLayout.RowSpacing = mobile ? 6 : 16;
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
        _queuePane.IsVisible = !_mobileCollections && !_showingPlayer && (desktop ? _queueOpen || _page == "queue" : _page == "queue");
        var display = _music.Pending ?? _music.Current;
        _desktopDock.IsVisible = display is not null && !_showingPlayer && Bounds.Width >= 1000 && !_mobileCollections;
        _mini.IsVisible = display is not null && !_showingPlayer && !_desktopDock.IsVisible && !(_mobileCollections && _page == "queue");
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
        if (_main.Child is Grid content)
        {
            content.Children[0].IsVisible = !_mobileCollections || _page is not ("queue" or "library");
            content.Children[1].IsVisible = !IsMobileWorkspace && !queue && !collection;
            content.RowSpacing = queue || collection || _page == "home" ? 4 : Bounds.Width < UiTheme.DesktopWidth ? 8 : 12;
        }
        if (_mobileCollections && queue) return;
        _pageTitle.Text = collection ? "Coleção" : queue ? "Sua fila" : _pageTitle.Text;
        var fingerprint = string.Join("|", _music.Queue.Select(t => t.Id + ":" + t.Source + _music.IsRecommended(t))) + ":" + _music.Current?.Source + ":" + (Bounds.Width < UiTheme.DesktopWidth) + ":" + _relatedTab;
        if (_queueFingerprint != fingerprint)
        {
            _queueFingerprint = fingerprint;
            var upcoming = _music.Upcoming.ToArray();
            if (_relatedTab) upcoming = upcoming.Where(_music.IsRecommended).ToArray();
            _upNext.ItemsSource = upcoming;
            var duration = upcoming.Sum(t => Math.Max(0, t.DurationSeconds));
            var mobile = Bounds.Width < UiTheme.DesktopWidth;
            var total = duration > 0 ? $" · {Time(duration)}" : "";
            _queueSummary.Text = upcoming.Length == 0 ? (_relatedTab ? "Nenhuma recomendação na fila. Busque novas sugestões no rádio." : "Nenhuma próxima faixa. Adicione músicas ou busque sugestões no rádio.") : $"{(mobile ? "" : "A SEGUIR · ")}{upcoming.Length} {(upcoming.Length == 1 ? "faixa" : "faixas")}{total}";
            var current = new StackPanel { Spacing = 12 };
            current.Children.Add(Text("TOCANDO AGORA", UiTheme.Label, Muted));
            if (_music.Current is { } track)
            {
                var row = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 12 };
                row.Children.Add(new Artwork(track, 64));
                var labels = new StackPanel { Spacing = 4 };
                var title = Text(TrackTextFormatter.Title(track.Title, track.Artist), UiTheme.Body); title.TextWrapping = TextWrapping.Wrap; title.MaxLines = 2; labels.Children.Add(title);
                var artist = Text(track.Artist, UiTheme.Caption, Muted); artist.TextTrimming = TextTrimming.CharacterEllipsis; labels.Children.Add(artist);
                Put(row, labels, 0, 1);
                if (mobile)
                {
                    row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                    var open = IconButton("forward", "Abrir faixa atual no player", () => { ShowPlayer(); return Task.CompletedTask; });
                    open.Classes.Add("subtle"); Put(row, open, 0, 2);
                }
                current.Children.Add(row);
            }
            else current.Children.Add(Text("Escolha uma música para começar.", UiTheme.Caption, Muted));
            _queueCurrent.Content = UiTheme.Card(current, 12);
        }
        RefreshDock();
        _queueTitle.Text = _music.PlaybackContext;
    }

    private Control QueueRow(Track track)
    {
        var section = Text(_music.IsRecommended(track) ? "RECOMENDADA" : "ADICIONADA POR VOCÊ", UiTheme.Label, Muted);
        var index = _music.Queue.IndexOf(track);
        section.IsVisible = index <= 0 || index == _music.CurrentQueueIndex + 1 || _music.IsRecommended(_music.Queue[index - 1]) != _music.IsRecommended(track);
        var grouped = new StackPanel { Spacing = 4 }; grouped.Children.Add(section); grouped.Children.Add(TrackRow(track));
        return grouped;
    }

    private void BuildDesktopDock()
    {
        var grid = new Grid { ColumnDefinitions = new("*,1.4*,*"), ColumnSpacing = 16 };
        var metadata = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 12 };
        metadata.Children.Add(_dockArtwork);
        var labels = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        _dockTitle.TextTrimming = _dockArtist.TextTrimming = TextTrimming.CharacterEllipsis; labels.Children.Add(_dockTitle); labels.Children.Add(_dockArtist); Put(metadata, labels, 0, 1);
        var open = ActionButton("", () => { ShowPlayer(); return Task.CompletedTask; }); open.Content = metadata; open.Classes.Add("subtle"); open.Padding = new Thickness(0); open.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Avalonia.Automation.AutomationProperties.SetName(open, "Expandir reprodução"); grid.Children.Add(open);
        var center = new Grid { RowDefinitions = new("Auto,Auto,Auto"), MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Stretch };
        var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 12 };
        _dockShuffle = IconButton("shuffle", "Embaralhar reprodução", TogglePlaybackShuffleAsync);
        controls.Children.Add(_dockShuffle);
        controls.Children.Add(IconButton("previous", "Faixa anterior", _music.PreviousAsync));
        _dockPlay = IconButton("play", "Reproduzir ou pausar", _music.ToggleAsync, UiTheme.OnPrimary); _dockPlay.Classes.Add("primary"); controls.Children.Add(_dockPlay);
        controls.Children.Add(IconButton("next", "Próxima faixa da fila", _music.NextAsync)); center.Children.Add(controls);
        _dockRepeat = IconButton("repeat", "Repetir", CyclePlaybackRepeatAsync);
        controls.Children.Add(_dockRepeat);
        _dockSeek.AddHandler(PointerPressedEvent, (_, _) => _dockSeeking = true, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _dockSeek.AddHandler(PointerReleasedEvent, (_, _) => { _dockSeeking = false; _music.Player.Seek(_dockSeek.Value); }, Avalonia.Interactivity.RoutingStrategies.Bubble, true);
        _dockSeek.KeyUp += (_, _) => _music.Player.Seek(_dockSeek.Value);
        Avalonia.Automation.AutomationProperties.SetName(_dockSeek, "Posição da reprodução");
        Put(center, _dockSeek, 1, 0); Put(grid, center, 0, 1);
        var times = new Grid { ColumnDefinitions = new("*,Auto") };
        times.Children.Add(_dockTime); Put(times, _dockDuration, 0, 1); Put(center, times, 2, 0);
        var rightHost = new Grid { RowDefinitions = new("Auto,Auto"), RowSpacing = 4, VerticalAlignment = VerticalAlignment.Center };
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _dockQuality.TextAlignment = TextAlignment.Right;
        _dockQuality.VerticalAlignment = VerticalAlignment.Center;
        _dockQuality.TextWrapping = TextWrapping.Wrap;
        _dockQuality.TextTrimming = TextTrimming.None;
        _dockQuality.TextAlignment = TextAlignment.Center;
        _dockQualityBadge = UiTheme.AudioBadge(_dockQuality);
        _dockQualityBadge.HorizontalAlignment = HorizontalAlignment.Center;
        rightHost.Children.Add(_dockQualityBadge);
        _dockFavorite = IconButton("heart", "Alternar favorito", TogglePlaybackFavoriteAsync);
        right.Children.Add(_dockFavorite);
        _sourceButton = IconButton("library", "Fonte de reprodução", ShowPlaybackSourcesAsync);
        _sourceButton.IsVisible = false;
        _sourceButton.VerticalAlignment = VerticalAlignment.Center;
        var sourceAndControls = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 12 };
        sourceAndControls.Children.Add(_sourceButton); Put(sourceAndControls, rightHost, 0, 1);
        right.Children.Add(IconButton("queue", "Mostrar ou ocultar fila", () => { _queueOpen = !_queueOpen; Refresh(); return Task.CompletedTask; }));
        var volume = _dockVolume = new Slider { Minimum = 0, Maximum = 1, Value = _music.Player.Volume, Width = 88, MinHeight = 48 };
        Avalonia.Automation.AutomationProperties.SetName(volume, "Volume da barra"); volume.ValueChanged += (_, _) => _music.Player.Volume = volume.Value;
        right.Children.Add(volume); Put(rightHost, right, 1, 0); Put(grid, sourceAndControls, 0, 2);
        _desktopDock.Child = grid; _desktopDock.Padding = new Thickness(20, 12, 20, 8); _desktopDock.Background = UiTheme.Brush(UiTheme.Surface);
        _desktopDock.BorderBrush = UiTheme.Brush(UiTheme.Outline); _desktopDock.BorderThickness = UiTheme.TopBorder;
    }

    private Task TogglePlaybackShuffleAsync()
    {
        _music.Shuffle = !_music.Shuffle; Refresh(); return Task.CompletedTask;
    }
    private Task CyclePlaybackRepeatAsync()
    {
        if (_music.Repeat) { _music.Repeat = false; _music.RepeatQueue = false; }
        else if (_music.RepeatQueue) { _music.RepeatQueue = false; _music.Repeat = true; }
        else _music.RepeatQueue = true;
        Refresh(); return Task.CompletedTask;
    }
    private async Task TogglePlaybackFavoriteAsync()
    {
        if ((_music.Pending ?? _music.Current) is { } track) await _music.ToggleFavoriteAsync(track);
        RefreshPlaybackActions();
    }
    private void RefreshPlaybackActions()
    {
        if (_dockVolume is not null) _dockVolume.Value = _music.Player.Volume;
        if (_detailVolume is not null) _detailVolume.Value = _music.Player.Volume;
        var track = _music.Pending ?? _music.Current;
        var favorite = track is not null && _music.IsFavorite(track);
        if (_sourceButton is not null)
        {
            ToolTip.SetTip(_sourceButton, "Fonte: " + ((_music.ActivePlaybackTrack ?? track) is { } active ? PlaybackSources.Provider(active) : "Original"));
            _sourceButton.IsEnabled = track is { IsLocal: false } && !_music.LoadingAudio;
        }
        void Update(Button? button, string icon, bool active, string label, bool repeatOne = false)
        {
            if (button is null) return;
            var state = (icon, active, repeatOne);
            if (!Equals(button.Tag, state))
            {
                button.Tag = state;
                button.Content = icon == "heart" ? Icons.Make(icon, active ? Accent : Muted, filled: active) : ToggleIcon(icon, active, repeatOne);
            }
            ToolTip.SetTip(button, label);
            Avalonia.Automation.AutomationProperties.SetName(button, label);
            button.IsEnabled = icon != "heart" || track is not null;
        }
        var repeatLabel = _music.Repeat ? "Repetir: faixa" : _music.RepeatQueue ? "Repetir: fila" : "Repetir: desligado";
        foreach (var button in new[] { _dockShuffle, _detailShuffle })
            Update(button, "shuffle", _music.Shuffle, _music.Shuffle ? "Aleatório: ligado" : "Aleatório: desligado");
        foreach (var button in new[] { _dockRepeat, _detailRepeat })
            Update(button, "repeat", _music.Repeat || _music.RepeatQueue, repeatLabel, _music.Repeat);
        foreach (var button in new[] { _dockFavorite, _detailFavorite })
            Update(button, "heart", favorite, favorite ? "Remover dos favoritos" : "Adicionar aos favoritos");
    }
    private void RefreshDock()
    {
        if (_dockPlay is null || !_desktopDock.IsVisible && !_showingPlayer) return;
        RefreshPlayButton(_dockPlay);
        var track = _music.Pending ?? _music.Current;
        if (!Equals(track, _dockTrack)) { _dockTrack = track; _dockArtwork.Content = new Artwork(track, 56); }
        _dockTitle.Text = TrackTextFormatter.Title(track?.Title, track?.Artist); _dockArtist.Text = track?.Artist;
        RefreshPlaybackActions();
        var duration = PlaybackDuration;
        _dockSeek.Maximum = Math.Max(1, duration);
        if (!_dockSeeking) _dockSeek.Value = Math.Clamp(_music.Position, 0, _dockSeek.Maximum);
        _dockTime.Text = Time(_music.Position);
        _dockDuration.Text = Time(duration);
        var source = _music.CurrentSource;
        var quality = new List<string>();
        if (!string.IsNullOrWhiteSpace(source?.Codec)) quality.Add(source.Codec.ToUpperInvariant());
        else if (!string.IsNullOrWhiteSpace(source?.Container)) quality.Add(source.Container.ToUpperInvariant());
        if (source?.BitDepth is > 0) quality.Add($"{source.BitDepth} bits");
        if (source?.BitrateKbps is > 0) quality.Add($"{source.BitrateKbps:0.#} kb/s");
        if (source?.SampleRateHz is > 0) quality.Add($"{source.SampleRateHz / 1000:0.#} kHz");
        var summary = string.Join(" · ", quality);
        _dockQuality.Text = summary;
        _dockQuality.IsVisible = summary.Length > 0;
        _dockQualityBadge.IsVisible = summary.Length > 0;
        ToolTip.SetTip(_dockQuality, summary);
        if (_expandedQuality is not null)
        {
            _expandedQuality.Text = summary;
            if (_expandedQuality.Parent is Border badge) badge.IsVisible = summary.Length > 0;
        }
    }

    private double PlaybackDuration => _music.Player.Duration > 0 ? _music.Player.Duration : _music.Current?.DurationSeconds ?? 0;

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
        actions.Children.Add(ActionButton("Adicionar à fila", () => { foreach (var track in _collectionTracks) _music.Enqueue(track); _music.SetStatus("Playlist adicionada à fila."); return Task.CompletedTask; }));
        actions.Children.Add(ActionButton("Voltar ao início", () => { _page = "home"; Refresh(); return Task.CompletedTask; })); Put(grid, actions, 1, 0);
        var list = new ItemsControl { ItemsSource = tracks, ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()), ItemTemplate = new FuncDataTemplate<Track>((track, _) => track is null ? new Border() : TrackRow(track)) };
        Put(grid, UiTheme.FadingScroll(list), 2, 0);
        _collectionPage.Child = grid; _page = "collection"; CloseSheet(); Refresh();
    }
}
