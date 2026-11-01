using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Hibiki.Core.Models;
using Hibiki.Core.Services;
using Track = Hibiki.Core.Models.Track;

namespace Hibiki.App.Views;

/// <summary>
/// Purpose-built desktop shell. Unlike the mobile view it provides persistent
/// navigation, a real workspace, a now-playing inspector and bottom transport.
/// No playback or catalog implementation is duplicated here.
/// </summary>
public sealed class DesktopView : UserControl
{
    private readonly MusicController _music;
    private readonly Grid _workspace = new() { ColumnDefinitions = new("228,*,320") };
    private readonly Border _inspector;
    private readonly TextBox _search = new()
    {
        Watermark = "Música, artista ou cole um link do YouTube",
        Height = 44, MinHeight = 44, FontSize = 14
    };
    private readonly StackPanel _results = new() { Spacing = 8 };
    private readonly TextBlock _pageTitle = DesktopTheme.Text("Descobrir músicas", 26, weight: FontWeight.SemiBold);
    private readonly TextBlock _pageSubtitle = DesktopTheme.Text("Encontre sua próxima faixa favorita.", 14, DesktopTheme.TextSecondary);
    private readonly TextBlock _resultCount = DesktopTheme.Text("", 13, DesktopTheme.TextMuted);
    private readonly TextBlock _status = DesktopTheme.Text("", 12, DesktopTheme.TextSecondary);
    private readonly TextBlock _footerTitle = DesktopTheme.Text("Nenhuma música selecionada", 14, weight: FontWeight.SemiBold);
    private readonly TextBlock _footerArtist = DesktopTheme.Text("Escolha uma faixa para começar", 12, DesktopTheme.TextMuted);
    private readonly TextBlock _sideTitle = DesktopTheme.Text("Nada tocando agora", 19, weight: FontWeight.SemiBold);
    private readonly TextBlock _sideArtist = DesktopTheme.Text("Suas músicas aparecerão aqui.", 13, DesktopTheme.TextSecondary);
    private readonly TextBlock _queueCount = DesktopTheme.Text("", 12, DesktopTheme.TextSecondary);
    private readonly StackPanel _queuePreview = new() { Spacing = 8 };
    private readonly ContentControl _artworkLarge = new();
    private readonly ContentControl _artworkSmall = new();
    private readonly Button _play = DesktopTheme.Button("", true);
    private readonly Button _favorite = DesktopTheme.Icon("heart", "Adicionar aos favoritos");
    private readonly Button _shuffle = DesktopTheme.Icon("shuffle", "Ativar aleatório");
    private readonly Button _repeat = DesktopTheme.Icon("repeat", "Ativar repetição");
    private readonly TextBlock _elapsed = DesktopTheme.Text("0:00", 12, DesktopTheme.TextMuted);
    private readonly TextBlock _duration = DesktopTheme.Text("0:00", 12, DesktopTheme.TextMuted);
    private readonly Slider _seek = new() { Minimum = 0, Maximum = 1, MinWidth = 180, Height = 32 };
    private readonly Dictionary<string, Button> _nav = new();
    private readonly Border _dialogLayer = new() { IsVisible = false, Background = Brush.Parse("#D9000000") };
    private readonly Border _contentPanel;
    private string _page = "explore";
    private string? _fingerprint;
    private string? _shownTrackId;
    private bool _seeking;

    public DesktopView(MusicController music)
    {
        _music = music;
        Background = DesktopTheme.Background;
        var shell = new Grid { RowDefinitions = new("*,112") };
        _workspace.Children.Add(BuildSidebar());
        _contentPanel = BuildWorkspace();
        Grid.SetColumn(_contentPanel, 1);
        _workspace.Children.Add(_contentPanel);
        _inspector = BuildInspector();
        Grid.SetColumn(_inspector, 2);
        _workspace.Children.Add(_inspector);
        shell.Children.Add(_workspace);

        var footer = BuildTransport();
        Grid.SetRow(footer, 1);
        shell.Children.Add(footer);
        _dialogLayer.Child = new Border();
        Grid.SetRowSpan(_dialogLayer, 2);
        shell.Children.Add(_dialogLayer);
        Content = shell;

        _seek.AddHandler(PointerPressedEvent, (_, _) => _seeking = true, RoutingStrategies.Tunnel);
        _seek.AddHandler(PointerReleasedEvent, (_, _) =>
        {
            _seeking = false;
            _music.Player.Seek(_seek.Value);
        }, RoutingStrategies.Bubble, true);
        _seek.KeyUp += (_, _) => _music.Player.Seek(_seek.Value);

        SizeChanged += (_, _) =>
        {
            // Hide the inspector only when space is constrained; the transport
            // remains visible at every supported desktop window size.
            var wide = Bounds.Width >= 1140;
            _workspace.ColumnDefinitions[0].Width = new GridLength(Bounds.Width >= 950 ? 228 : 194);
            _workspace.ColumnDefinitions[2].Width = new GridLength(wide ? 320 : 0);
            _inspector.IsVisible = wide;
        };
        KeyDown += async (_, args) =>
        {
            if (args.Key == Key.Escape && _dialogLayer.IsVisible)
            {
                CloseDialog(); args.Handled = true;
            }
            else if (args.Key == Key.F && args.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                SelectPage("explore"); _search.Focus(); args.Handled = true;
            }
            else if (args.Key == Key.Space && _search.IsFocused == false
                && !_dialogLayer.IsVisible)
            {
                args.Handled = true; await _music.ToggleAsync();
            }
        };
        AppServices.BackHandler = () =>
        {
            if (!_dialogLayer.IsVisible) return false;
            CloseDialog(); return true;
        };
        AppServices.OpenPlayerView = () => SelectPage("queue");
        AttachedToVisualTree += async (_, _) =>
        {
            _music.Changed -= Refresh;
            _music.Tick -= UpdateTransport;
            _music.Changed += Refresh;
            _music.Tick += UpdateTransport;
            _music.StartObserving();
            _fingerprint = null;
            Refresh();
            await _music.InitializeAsync();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _music.Changed -= Refresh;
            _music.Tick -= UpdateTransport;
            _music.StopObserving();
        };
        Refresh();
    }

    private Border BuildSidebar()
    {
        var sidebar = new Grid
        {
            RowDefinitions = new("Auto,Auto,*,Auto"),
            Margin = new Thickness(18, 24, 18, 20)
        };
        var brand = new StackPanel { Spacing = 7 };
        var brandRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 11 };
        brandRow.Children.Add(Icons.Make("wave", DesktopTheme.Accent, 28));
        brandRow.Children.Add(DesktopTheme.Text("Hibiki", 24, weight: FontWeight.SemiBold));
        brand.Children.Add(brandRow);
        brand.Children.Add(DesktopTheme.Text("SUA MÚSICA. SEU RITMO.", 10, DesktopTheme.TextMuted, FontWeight.Medium));
        sidebar.Children.Add(brand);

        var menu = new StackPanel { Spacing = 8, Margin = new Thickness(0, 36, 0, 0) };
        menu.Children.Add(DesktopTheme.Text("NAVEGAR", 11, DesktopTheme.TextMuted, FontWeight.SemiBold));
        menu.Children.Add(Nav("explore", "Buscar músicas", "search"));
        menu.Children.Add(Nav("library", "Minha biblioteca", "heart"));
        menu.Children.Add(Nav("queue", "Fila de reprodução", "queue"));
        Grid.SetRow(menu, 1);
        sidebar.Children.Add(menu);

        var bottom = new StackPanel { Spacing = 13 };
        bottom.Children.Add(DesktopTheme.Separator());
        var settings = Button("Configurações e diagnóstico", () => { ShowSettings(); return Task.CompletedTask; });
        settings.HorizontalContentAlignment = HorizontalAlignment.Left;
        settings.Background = DesktopTheme.Sidebar;
        settings.BorderThickness = new Thickness(0);
        settings.FontSize = 12;
        bottom.Children.Add(settings);
        _status.TextWrapping = TextWrapping.Wrap;
        _status.MaxLines = 3;
        bottom.Children.Add(_status);
        bottom.Children.Add(DesktopTheme.Text("HIBIKI  ·  DESKTOP", 10, DesktopTheme.TextMuted));
        Grid.SetRow(bottom, 3);
        sidebar.Children.Add(bottom);

        return new Border
        {
            Background = DesktopTheme.Sidebar,
            BorderBrush = DesktopTheme.Outline,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = sidebar
        };
    }

    private Button Nav(string key, string label, string icon)
    {
        var button = Button("", () => { SelectPage(key); return Task.CompletedTask; });
        button.Height = 46; button.MinHeight = 46;
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.Padding = new Thickness(13, 8);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        row.Children.Add(Icons.Make(icon, DesktopTheme.TextSecondary, 21));
        row.Children.Add(DesktopTheme.Text(label, 13, DesktopTheme.TextPrimary, FontWeight.Medium));
        button.Content = row;
        _nav[key] = button;
        return button;
    }

    private Border BuildWorkspace()
    {
        var page = new Grid { RowDefinitions = new("Auto,Auto,*"), RowSpacing = 22, Margin = new Thickness(30, 28, 30, 20) };
        var header = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 14 };
        var heading = new StackPanel { Spacing = 8 };
        heading.Children.Add(_pageTitle);
        heading.Children.Add(_pageSubtitle);
        header.Children.Add(heading);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(Button("Abrir áudio local", OpenLocalAsync));
        actions.Children.Add(Button("Configurações", () => { ShowSettings(); return Task.CompletedTask; }));
        Grid.SetColumn(actions, 1); header.Children.Add(actions);
        page.Children.Add(header);

        var searchCard = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 12 };
        _search.VerticalContentAlignment = VerticalAlignment.Center;
        _search.Background = DesktopTheme.Surface;
        _search.BorderBrush = DesktopTheme.Outline;
        _search.BorderThickness = new Thickness(1);
        _search.CornerRadius = new CornerRadius(12);
        _search.Padding = new Thickness(14, 6);
        _search.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; await SearchAsync(); }
        };
        searchCard.Children.Add(_search);
        var search = Button("Buscar", SearchAsync, primary: true);
        search.Width = 108; search.Height = 44; search.MinHeight = 44;
        Grid.SetColumn(search, 1); searchCard.Children.Add(search);
        Grid.SetRow(searchCard, 1); page.Children.Add(searchCard);

        var section = new Grid { RowDefinitions = new("Auto,*"), RowSpacing = 13 };
        var head = new Grid { ColumnDefinitions = new("*,Auto") };
        head.Children.Add(DesktopTheme.Text("FAIXAS", 11, DesktopTheme.TextMuted, FontWeight.SemiBold));
        Grid.SetColumn(_resultCount, 1); head.Children.Add(_resultCount);
        section.Children.Add(head);
        var scroll = new ScrollViewer
        {
            Content = _results, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        Grid.SetRow(scroll, 1); section.Children.Add(scroll);
        Grid.SetRow(section, 2); page.Children.Add(section);

        return new Border
        {
            Background = DesktopTheme.Background,
            Child = new Border { MaxWidth = 1150, HorizontalAlignment = HorizontalAlignment.Stretch, Child = page }
        };
    }

    private Border BuildInspector()
    {
        var content = new StackPanel { Spacing = 16, Margin = new Thickness(22, 28) };
        content.Children.Add(DesktopTheme.Text("TOCANDO AGORA", 11, DesktopTheme.TextMuted, FontWeight.SemiBold));
        _artworkLarge.Content = new Artwork(null, 240);
        _artworkLarge.HorizontalAlignment = HorizontalAlignment.Center;
        content.Children.Add(_artworkLarge);
        _sideTitle.TextWrapping = TextWrapping.Wrap;
        _sideTitle.MaxLines = 2;
        content.Children.Add(_sideTitle);
        content.Children.Add(_sideArtist);

        var save = Button("Salvar nos favoritos", async () =>
        {
            if (_music.Current is { } track) await _music.ToggleFavoriteAsync(track);
        });
        save.HorizontalAlignment = HorizontalAlignment.Stretch;
        var favoriteRow = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 8 };
        favoriteRow.Children.Add(_favorite);
        _favorite.Click += async (_, _) =>
        {
            if (_music.Current is { } track) await _music.ToggleFavoriteAsync(track);
        };
        Grid.SetColumn(save, 1); favoriteRow.Children.Add(save);
        content.Children.Add(favoriteRow);
        content.Children.Add(DesktopTheme.Separator());
        var queueHeader = new Grid { ColumnDefinitions = new("*,Auto") };
        queueHeader.Children.Add(DesktopTheme.Text("PRÓXIMAS NA FILA", 11, DesktopTheme.TextMuted, FontWeight.SemiBold));
        Grid.SetColumn(_queueCount, 1); queueHeader.Children.Add(_queueCount);
        content.Children.Add(queueHeader);
        content.Children.Add(_queuePreview);

        return new Border
        {
            Background = DesktopTheme.Sidebar,
            BorderBrush = DesktopTheme.Outline,
            BorderThickness = new Thickness(1, 0, 0, 0),
            Child = new ScrollViewer
            {
                Content = content,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            }
        };
    }

    private Border BuildTransport()
    {
        var body = new Grid
        {
            ColumnDefinitions = new("245,*,320"), ColumnSpacing = 16,
            Margin = new Thickness(20, 14)
        };
        var track = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        _artworkSmall.Content = new Artwork(null, 54);
        track.Children.Add(_artworkSmall);
        var details = new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Width = 172 };
        _footerTitle.MaxLines = 1;
        details.Children.Add(_footerTitle); details.Children.Add(_footerArtist);
        track.Children.Add(details); body.Children.Add(track);

        var transport = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 11,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        _shuffle.Click += (_, _) => { _music.Shuffle = !_music.Shuffle; Refresh(); };
        _repeat.Click += (_, _) => { _music.Repeat = !_music.Repeat; Refresh(); };
        transport.Children.Add(_shuffle);
        transport.Children.Add(IconAction("previous", "Faixa anterior", _music.PreviousAsync));
        _play.Content = Icons.Make("play", DesktopTheme.OnAccent, 23);
        _play.Width = _play.Height = 52; _play.MinHeight = 52; _play.MinWidth = 52;
        _play.CornerRadius = new CornerRadius(26);
        _play.Click += async (_, _) => await _music.ToggleAsync();
        transport.Children.Add(_play);
        transport.Children.Add(IconAction("next", "Próxima faixa", _music.NextAsync));
        transport.Children.Add(_repeat);
        Grid.SetColumn(transport, 1); body.Children.Add(transport);

        var progress = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
        _seek.Foreground = DesktopTheme.Accent; _seek.Background = DesktopTheme.Outline;
        progress.Children.Add(_seek);
        var times = new Grid { ColumnDefinitions = new("*,Auto") };
        times.Children.Add(_elapsed);
        Grid.SetColumn(_duration, 1); times.Children.Add(_duration);
        progress.Children.Add(times);
        Grid.SetColumn(progress, 2); body.Children.Add(progress);
        return new Border
        {
            Background = DesktopTheme.Surface,
            BorderBrush = DesktopTheme.Outline,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = body
        };
    }

    private Button IconAction(string icon, string label, Func<Task> action)
    {
        var button = DesktopTheme.Icon(icon, label, 22);
        button.Click += async (_, _) => await Safely(action);
        return button;
    }

    private Button Button(string label, Func<Task> action, bool primary = false)
    {
        var button = DesktopTheme.Button(label, primary);
        button.Click += async (_, _) => await Safely(action);
        return button;
    }

    private async Task Safely(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { _music.SetStatus(ex.Message); }
    }

    private void SelectPage(string page)
    {
        _page = page;
        _fingerprint = null;
        Refresh();
    }

    private async Task SearchAsync()
    {
        _page = "explore";
        Refresh();
        await _music.SearchAsync(_search.Text ?? "");
    }

    private void Refresh()
    {
        _status.Text = _music.Status;
        var tracks = _page switch
        {
            "library" => _music.Favorites,
            "queue" => _music.Queue,
            _ => _music.Results
        };
        _pageTitle.Text = _page switch
        {
            "library" => "Sua biblioteca",
            "queue" => "Fila de reprodução",
            _ => "Descubra sua música"
        };
        _pageSubtitle.Text = _page switch
        {
            "library" => "As faixas que você guardou, sempre à mão.",
            "queue" => "Organize o que você vai ouvir.",
            _ => "Busque músicas, artistas ou links do YouTube."
        };
        _resultCount.Text = tracks.Count == 1 ? "1 faixa" : tracks.Count + " faixas";
        foreach (var (key, button) in _nav)
        {
            var active = key == _page;
            button.Background = active ? DesktopTheme.AccentSoft : Brushes.Transparent;
            button.BorderThickness = new Thickness(0);
            if (button.Content is StackPanel panel)
            {
                foreach (var label in panel.Children.OfType<TextBlock>())
                    label.Foreground = active ? DesktopTheme.Accent : DesktopTheme.TextSecondary;
                foreach (var icon in panel.Children.OfType<Avalonia.Controls.Shapes.Path>())
                {
                    icon.Stroke = active ? DesktopTheme.Accent : DesktopTheme.TextSecondary;
                    if (icon.Fill is not null) icon.Fill = icon.Stroke;
                }
            }
        }

        var fingerprint = _page + "|" + string.Join("|",
            tracks.Select(t => t.Id + ":" + _music.IsFavorite(t))) +
            "|" + (_music.Current?.Id ?? "");
        if (fingerprint != _fingerprint)
        {
            _fingerprint = fingerprint;
            _results.Children.Clear();
            if (tracks.Count == 0) _results.Children.Add(EmptyState());
            else
            {
                for (var index = 0; index < tracks.Count; index++)
                    _results.Children.Add(TrackRow(tracks[index], index));
            }
        }

        var playing = _music.Current ?? _music.Pending;
        if (_shownTrackId != playing?.Id)
        {
            _shownTrackId = playing?.Id;
            _artworkLarge.Content = new Artwork(playing, 240);
            _artworkSmall.Content = new Artwork(playing, 54);
        }
        var name = playing is null ? "Nenhuma música selecionada" :
            TrackTextFormatter.Title(playing.Title, playing.Artist);
        var artist = playing is null ? "Escolha uma faixa para começar" :
            TrackTextFormatter.Artist(playing.Artist);
        _footerTitle.Text = _sideTitle.Text = name;
        _footerArtist.Text = _sideArtist.Text = artist;
        _favorite.IsEnabled = _music.Current is not null;
        _favorite.Content = Icons.Make("heart_filled",
            _music.Current is not null && _music.IsFavorite(_music.Current)
                ? DesktopTheme.Accent : DesktopTheme.TextSecondary, 20);
        _shuffle.Content = Icons.Make("shuffle", _music.Shuffle ? DesktopTheme.Accent : DesktopTheme.TextSecondary, 21);
        _repeat.Content = Icons.Make("repeat", _music.Repeat ? DesktopTheme.Accent : DesktopTheme.TextSecondary, 21);
        _queueCount.Text = _music.Queue.Count.ToString();
        _queuePreview.Children.Clear();
        if (_music.Queue.Count == 0)
            _queuePreview.Children.Add(DesktopTheme.Text("Sua fila está vazia.", 13, DesktopTheme.TextMuted));
        else
        {
            foreach (var next in _music.Queue.Take(5))
            {
                var item = next;
                var button = Button("", () => _music.PlayAsync(item));
                button.Height = 44; button.MinHeight = 44;
                button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                button.Background = item.Id == playing?.Id ? DesktopTheme.AccentSoft : DesktopTheme.Surface;
                button.BorderThickness = new Thickness(0);
                var label = DesktopTheme.Text(TrackTextFormatter.Title(item.Title, item.Artist), 12);
                label.MaxLines = 1;
                button.Content = label;
                _queuePreview.Children.Add(button);
            }
        }
        UpdateTransport();
    }

    private Control EmptyState()
    {
        var empty = new StackPanel
        {
            Spacing = 14, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 75)
        };
        var icon = Icons.Make(_page == "library" ? "heart" : "wave", DesktopTheme.Accent, 38);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        empty.Children.Add(icon);
        var title = DesktopTheme.Text(_page switch
        {
            "library" => "Sua biblioteca começa aqui",
            "queue" => "Sua fila está vazia",
            _ => "Pronto para descobrir algo novo?"
        }, 20, weight: FontWeight.SemiBold);
        title.TextAlignment = TextAlignment.Center;
        empty.Children.Add(title);
        var note = DesktopTheme.Text(_page switch
        {
            "library" => "Salve suas músicas favoritas para encontrá-las rapidamente.",
            "queue" => "Adicione músicas à fila enquanto navega pelos resultados.",
            _ => "Digite o nome de uma música ou artista acima para começar."
        }, 14, DesktopTheme.TextSecondary);
        note.TextAlignment = TextAlignment.Center;
        empty.Children.Add(note);
        var action = Button("Abrir música do computador", OpenLocalAsync);
        action.HorizontalAlignment = HorizontalAlignment.Center;
        action.Margin = new Thickness(0, 10, 0, 0);
        empty.Children.Add(action);
        return DesktopTheme.Card(empty, 18);
    }

    private Control TrackRow(Track track, int index)
    {
        var isCurrent = track.Id == (_music.Current ?? _music.Pending)?.Id;
        var row = new Grid
        {
            ColumnDefinitions = new("*,78,48,48"),
            ColumnSpacing = 8, MinHeight = 68
        };
        var select = Button("", () => _music.PlayAsync(track));
        select.MinHeight = 62; select.Height = 62;
        select.Padding = new Thickness(10, 5);
        select.Background = Brushes.Transparent;
        select.BorderThickness = new Thickness(0);
        select.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        var metadata = new Grid { ColumnDefinitions = new("54,*"), ColumnSpacing = 14 };
        metadata.Children.Add(new Artwork(track, 50));
        var labels = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        var name = DesktopTheme.Text(TrackTextFormatter.Title(track.Title, track.Artist), 14,
            isCurrent ? DesktopTheme.Accent : DesktopTheme.TextPrimary, FontWeight.SemiBold);
        name.MaxLines = 1;
        labels.Children.Add(name);
        labels.Children.Add(DesktopTheme.Text(TrackTextFormatter.Artist(track.Artist), 12, DesktopTheme.TextSecondary));
        Grid.SetColumn(labels, 1);
        metadata.Children.Add(labels);
        select.Content = metadata;
        row.Children.Add(select);
        var duration = DesktopTheme.Text(track.DurationLabel, 12, DesktopTheme.TextMuted);
        duration.VerticalAlignment = VerticalAlignment.Center;
        duration.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(duration, 1);
        row.Children.Add(duration);
        var like = IconAction(_music.IsFavorite(track) ? "heart_filled" : "heart",
            "Alternar favorito", () => _music.ToggleFavoriteAsync(track));
        Grid.SetColumn(like, 2); row.Children.Add(like);
        var queue = IconAction("plus", "Adicionar à fila", () =>
        {
            _music.Enqueue(track);
            return Task.CompletedTask;
        });
        Grid.SetColumn(queue, 3); row.Children.Add(queue);
        return DesktopTheme.Card(row, 4, isCurrent ? DesktopTheme.AccentSoft : DesktopTheme.Surface);
    }

    private void UpdateTransport()
    {
        var current = _music.Current ?? _music.Pending;
        var icon = _music.LoadingAudio ? "close" : _music.Player.IsPlaying ? "pause" : "play";
        _play.Content = Icons.Make(icon, DesktopTheme.OnAccent, 25);
        var length = _music.Player.Duration > 0 ? _music.Player.Duration : current?.DurationSeconds ?? 0;
        _seek.IsEnabled = length > 0 && !_music.LoadingAudio;
        _seek.Maximum = Math.Max(1, length);
        if (!_seeking) _seek.Value = Math.Clamp(_music.Player.Position, 0, _seek.Maximum);
        _elapsed.Text = TrackTextFormatter.Duration(_music.Player.Position);
        _duration.Text = TrackTextFormatter.Duration(length, unknownWhenZero: true);
    }

    private async Task OpenLocalAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null || !top.StorageProvider.CanOpen)
        {
            _music.SetStatus("Seletor de arquivos indisponível."); return;
        }
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Selecione arquivos de áudio", AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("Áudio")
            {
                Patterns = ["*.mp3", "*.m4a", "*.aac", "*.ogg", "*.opus", "*.flac", "*.wav"],
                MimeTypes = ["audio/*"]
            }]
        });
        Track? first = null;
        foreach (var file in files)
        {
            using (file)
            {
                var id = Guid.NewGuid().ToString("N");
                var directory = Path.Combine(_music.DataDirectory, "audio");
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, id + Path.GetExtension(file.Name));
                try
                {
                    await using var input = await file.OpenReadAsync();
                    await using var output = File.Create(path);
                    await input.CopyToAsync(output);
                }
                catch { if (File.Exists(path)) File.Delete(path); throw; }
                var track = new Track(id, Path.GetFileNameWithoutExtension(file.Name),
                    "Áudio local", path, IsLocal: true);
                await _music.ToggleFavoriteAsync(track);
                first ??= track;
            }
        }
        SelectPage("library");
        if (first is not null) await _music.PlayAsync(first);
    }

    private void ShowSettings()
    {
        var content = new StackPanel { Spacing = 15 };
        content.Children.Add(DesktopTheme.Text("Configurações", 24, weight: FontWeight.SemiBold));
        content.Children.Add(DesktopTheme.Text("Reprodução, sessão e diagnóstico",
            13, DesktopTheme.TextSecondary));
        content.Children.Add(DesktopTheme.Separator());
        content.Children.Add(DesktopTheme.Text("YOUTUBE", 11, DesktopTheme.TextMuted, FontWeight.SemiBold));
        var cookiePath = Path.Combine(_music.DataDirectory, "youtube-cookies.txt");
        content.Children.Add(DesktopTheme.Text(File.Exists(cookiePath)
            ? "Uma sessão foi importada neste computador."
            : "Sem sessão importada. Algumas faixas podem exigir verificação.",
            13, DesktopTheme.TextSecondary));
        content.Children.Add(Button("Importar cookies Netscape", ImportCookiesAsync));
        if (File.Exists(cookiePath))
            content.Children.Add(Button("Remover sessão importada", () =>
            {
                File.Delete(cookiePath);
                AppServices.InvalidateCatalog();
                ShowSettings(); return Task.CompletedTask;
            }));
        content.Children.Add(DesktopTheme.Separator());
        content.Children.Add(DesktopTheme.Text("DIAGNÓSTICO", 11, DesktopTheme.TextMuted, FontWeight.SemiBold));
        var diagnostic = new TextBox
        {
            Text = PlaybackDiagnostics.Snapshot(),
            IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            MinHeight = 110, MaxHeight = 190, FontSize = 12,
            Background = DesktopTheme.Raised,
            BorderBrush = DesktopTheme.Outline
        };
        content.Children.Add(diagnostic);
        content.Children.Add(Button("Copiar diagnóstico", async () =>
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null) await clipboard.SetTextAsync(PlaybackDiagnostics.Snapshot());
        }));
        content.Children.Add(Button("Fechar", () => { CloseDialog(); return Task.CompletedTask; }, true));
        var card = DesktopTheme.Card(new ScrollViewer
        {
            Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        }, 26);
        card.Width = 520; card.MaxHeight = 620;
        card.VerticalAlignment = VerticalAlignment.Center;
        card.HorizontalAlignment = HorizontalAlignment.Center;
        card.Margin = new Thickness(18);
        _dialogLayer.Child = card;
        _dialogLayer.IsVisible = true;
    }

    private async Task ImportCookiesAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;
        var selection = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Cookies Netscape", AllowMultiple = false
        });
        if (selection.Count == 0) return;
        using var file = selection[0];
        await using var stream = await file.OpenReadAsync();
        using var reader = new StreamReader(stream);
        var buffer = new char[1_000_001];
        var count = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
        if (count > 1_000_000) throw new IOException("Arquivo de cookies grande demais.");
        var text = new string(buffer, 0, count);
        if (!text.StartsWith("# Netscape HTTP Cookie File") &&
            !text.StartsWith("# HTTP Cookie File"))
            throw new IOException("O arquivo precisa estar no formato Netscape.");
        Directory.CreateDirectory(_music.DataDirectory);
        await File.WriteAllTextAsync(Path.Combine(_music.DataDirectory, "youtube-cookies.txt"), text);
        AppServices.InvalidateCatalog();
        _music.SetStatus("Sessão importada.");
        ShowSettings();
    }

    private void CloseDialog() => _dialogLayer.IsVisible = false;
}
