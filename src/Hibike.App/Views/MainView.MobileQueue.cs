using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Hibike.Core.Models;

namespace Hibike.App.Views;

public sealed partial class MainView
{
    private string _mobileQueueReturnPage = "home";
    private sealed record MobileQueueRow(Track Track, bool Recommended);
    private Button _mobileQueueBack = null!, _mobileQueueRetry = null!, _mobileQueuePlay = null!, _mobileQueueShuffle = null!;
    private ContentControl _mobileQueueCover = null!, _mobileQueueEmpty = null!;
    private TextBlock _mobileQueueTitle = null!, _mobileQueueArtist = null!, _mobileQueueRadioStatus = null!, _mobileQueueSummary = null!;
    private TextBlock _mobileQueueHeader = null!, _mobileQueueError = null!, _mobileQueueSection = null!;
    private Grid _mobileQueueRadio = null!;
    private ItemsControl _mobileQueueItems = null!;
    private ScrollViewer _mobileQueueScroll = null!;
    private Track? _mobileQueueCurrent;
    private MobileQueueRow[] _mobileQueueRows = [];

    private Grid BuildMobileQueue()
    {
        var page = new Grid { RowDefinitions = new("Auto,Auto,Auto,Auto,Auto,*"), RowSpacing = 12 };
        var header = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 4, Margin = new Thickness(0, 4, 0, 0) };
        _mobileQueueBack = CollectionIcon("back", "Voltar ao player", () => { ReturnFromMobileQueue(); return Task.CompletedTask; });
        header.Children.Add(_mobileQueueBack);
        _mobileQueueHeader = CollectionText("Fila de reprodução", 27, weight: FontWeight.SemiBold);
        Put(header, _mobileQueueHeader, 0, 1);
        Put(header, CollectionIcon("more", "Opções da fila", () =>
        {
            ShowMobileQueueOptions();
            return Task.CompletedTask;
        }), 0, 2);
        page.Children.Add(header);

        var current = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 12 };
        _mobileQueueCover = new ContentControl { Content = new Artwork(null, 56), VerticalAlignment = VerticalAlignment.Center };
        current.Children.Add(_mobileQueueCover);
        var metadata = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        metadata.Children.Add(CollectionText("Tocando agora", UiTheme.Label, CollectionAccent, FontWeight.Medium));
        _mobileQueueTitle = CollectionText("", 16, weight: FontWeight.SemiBold);
        _mobileQueueArtist = CollectionText("", UiTheme.Caption, Muted);
        metadata.Children.Add(_mobileQueueTitle);
        metadata.Children.Add(_mobileQueueArtist);
        var openPlayer = ActionButton("", () => { ShowPlayer(); return Task.CompletedTask; });
        openPlayer.Classes.Add("subtle");
        openPlayer.Padding = UiTheme.Zero;
        openPlayer.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        openPlayer.Content = metadata;
        AutomationProperties.SetName(openPlayer, "Abrir faixa atual no player");
        Put(current, openPlayer, 0, 1);
        _mobileQueuePlay = CollectionIcon("play", "Reproduzir ou pausar", _music.ToggleAsync, UiTheme.OnPrimary);
        _mobileQueuePlay.Classes.Add("primary");
        Put(current, _mobileQueuePlay, 0, 2);
        Put(page, new Border
        {
            Child = current, Padding = new Thickness(12, 10), CornerRadius = new(16),
            Background = UiTheme.Brush(UiTheme.SurfaceElevated)
        }, 1, 0);

        _mobileQueueRadio = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 10, Margin = new Thickness(2, 0) };
        var radioIcon = Icons.Make("radio", CollectionAccent, 20);
        radioIcon.VerticalAlignment = VerticalAlignment.Center;
        _mobileQueueRadio.Children.Add(radioIcon);
        var radioLabels = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        radioLabels.Children.Add(CollectionText("Rádio", UiTheme.Body, weight: FontWeight.SemiBold));
        _mobileQueueRadioStatus = CollectionText("", UiTheme.Caption, Muted);
        _mobileQueueRadioStatus.TextWrapping = TextWrapping.Wrap;
        _mobileQueueRadioStatus.MaxLines = 2;
        radioLabels.Children.Add(_mobileQueueRadioStatus);
        Put(_mobileQueueRadio, radioLabels, 0, 1);
        _mobileQueueRetry = CollectionIcon("refresh", "Buscar novas sugestões", _music.RetryRadioAsync);
        Put(_mobileQueueRadio, _mobileQueueRetry, 0, 2);
        Put(page, _mobileQueueRadio, 2, 0);

        var upcoming = new Grid { RowDefinitions = new("Auto,1"), ColumnDefinitions = new("*,Auto"), Margin = new Thickness(0, 4, 0, 0) };
        var heading = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        _mobileQueueSection = CollectionText("A seguir", 20, weight: FontWeight.SemiBold);
        heading.Children.Add(_mobileQueueSection);
        _mobileQueueSummary = CollectionText("", UiTheme.Caption, Muted);
        heading.Children.Add(_mobileQueueSummary);
        upcoming.Children.Add(heading);
        _mobileQueueShuffle = CollectionIcon("shuffle", "Embaralhar próximas faixas", () =>
        {
            _music.ReorderQueue(true);
            return Task.CompletedTask;
        }, Muted);
        Put(upcoming, _mobileQueueShuffle, 0, 1);
        var rule = CollectionRule();
        Put(upcoming, rule, 1, 0);
        Grid.SetColumnSpan(rule, 2);
        _mobileQueueError = CollectionText("", UiTheme.Caption, UiTheme.Error);
        _mobileQueueError.TextWrapping = TextWrapping.Wrap;
        _mobileQueueError.MaxLines = 2;
        Put(page, _mobileQueueError, 3, 0);
        Put(page, upcoming, 4, 0);

        _mobileQueueItems = CollectionList<MobileQueueRow>(item =>
        {
            var track = item.Track;
            var caption = TrackTextFormatter.Artist(track.Artist);
            if (item.Recommended) caption += " · Rádio";
            if (track.DurationSeconds > 0) caption += " · " + track.DurationLabel;
            return CollectionTrackRow(track, () => PlayMobileQueueTrackAsync(track), _ => ShowMobileQueueTrackOptions(track), caption, item.Recommended);
        });
        _mobileQueueScroll = UiTheme.FadingScroll(_mobileQueueItems);
        _mobileQueueEmpty = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch };
        Put(page, new Grid { Children = { _mobileQueueScroll, _mobileQueueEmpty } }, 5, 0);
        return page;
    }

    private void RefreshMobileQueue()
    {
        var current = _music.Pending ?? _music.Current;
        _mobileQueueBack.IsVisible = _queueOpenedFromPlayer;
        _mobileQueueHeader.Text = _queueOpenedFromPlayer ? "Fila" : "Fila de reprodução";
        _mobileQueueError.Text = _music.Status;
        _mobileQueueError.IsVisible = _music.HasError;
        if (!Equals(_mobileQueueCurrent, current))
        {
            _mobileQueueCurrent = current;
            _mobileQueueCover.Content = new Artwork(current, 56);
        }
        _mobileQueueTitle.Text = current is null ? "Nenhuma faixa selecionada" : TrackTextFormatter.Title(current.Title, current.Artist);
        _mobileQueueArtist.Text = current is null ? "Escolha uma música para começar" : TrackTextFormatter.Artist(current.Artist);
        _mobileQueuePlay.IsVisible = current is not null;
        RefreshMobileQueueTransport();

        _mobileQueueRadio.IsVisible = _music.Current is not null;
        _mobileQueueRadioStatus.Text = _music.RadioLoading ? "Buscando sugestões…"
            : _music.RadioStatus == "Sugestões aparecem quando próximas músicas acabam."
                ? (_music.Autoplay ? "Sugestões ao fim da fila" : "Reprodução automática desligada")
                : _music.RadioStatus;
        _mobileQueueRetry.IsEnabled = _music.Current is not null && !_music.RadioLoading && !_music.LoadingAudio;

        _mobileQueueSection.Text = _relatedTab ? "Recomendações" : "A seguir";
        var rows = _music.Upcoming.Select(track => new MobileQueueRow(track, _music.IsRecommended(track)))
            .Where(row => !_relatedTab || row.Recommended).ToArray();
        if (!_mobileQueueRows.SequenceEqual(rows))
        {
            _mobileQueueRows = rows;
            _mobileQueueItems.ItemsSource = rows;
        }
        var duration = rows.Sum(row => Math.Max(0, row.Track.DurationSeconds));
        _mobileQueueSummary.Text = TrackCount(rows.Length) + (duration > 0 ? $" · {Time(duration)}" : "");
        _mobileQueueShuffle.IsVisible = !_relatedTab;
        _mobileQueueShuffle.IsEnabled = rows.Length > 1;
        _mobileQueueScroll.IsVisible = rows.Length > 0;
        _mobileQueueEmpty.IsVisible = rows.Length == 0;
        if (rows.Length > 0) return;

        var canRetry = _music.Current is not null;
        if (Equals(_mobileQueueEmpty.Tag, (canRetry, _relatedTab))) return;
        _mobileQueueEmpty.Tag = (canRetry, _relatedTab);
        _mobileQueueEmpty.Content = _relatedTab
            ? CollectionEmpty("radio", "Nenhuma recomendação", "Use o rádio para buscar sugestões ou volte à fila completa.")
            : canRetry
            ? CollectionEmpty("queue", "Fim da fila", "Adicione músicas pelo menu de uma faixa ou use o rádio para buscar sugestões.")
            : CollectionEmpty("queue", "A fila está vazia", "Toque uma música ou adicione faixas pela biblioteca.",
                CollectionAction("Abrir biblioteca", () =>
                {
                    _page = "library";
                    Refresh();
                    return Task.CompletedTask;
                }, prominent: true));
    }

    private void RefreshMobileQueueTransport()
    {
        var transportIcon = _music.LoadingAudio ? "close" : _music.Player.IsPlaying ? "pause" : "play";
        if (!Equals(_mobileQueuePlay.Tag, transportIcon))
        {
            _mobileQueuePlay.Tag = transportIcon;
            _mobileQueuePlay.Content = Icons.Make(transportIcon, UiTheme.OnPrimary, 20);
            var label = transportIcon switch { "close" => "Cancelar carregamento", "pause" => "Pausar", _ => "Reproduzir" };
            AutomationProperties.SetName(_mobileQueuePlay, label);
            ToolTip.SetTip(_mobileQueuePlay, label);
        }
    }

    private Task PlayMobileQueueTrackAsync(Track track)
    {
        var index = _music.Queue.IndexOf(track);
        return index > _music.CurrentQueueIndex ? _music.PlayAsync(track, index) : Task.CompletedTask;
    }

    private void ShowMobileQueueTrackOptions(Track track)
    {
        var panel = new StackPanel { Spacing = 4 };
        var index = _music.Queue.IndexOf(track);
        var first = _music.CurrentQueueIndex + 1;
        if (index < first) return;
        Add("Mover para cima", -1, index > first);
        Add("Mover para baixo", 1, index < _music.Queue.Count - 1);
        Add("Remover da fila", 0, true);
        ShowSheet(TrackTextFormatter.Title(track.Title, track.Artist), panel);

        void Add(string label, int direction, bool enabled)
        {
            var action = CollectionAction(label, () =>
            {
                if (_music.Queue.IndexOf(track) > _music.CurrentQueueIndex) _music.EditQueue(track, direction);
                CloseSheet();
                return Task.CompletedTask;
            });
            action.IsEnabled = enabled;
            panel.Children.Add(action);
        }
    }

    private void ShowMobileQueueOptions()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(CreateRadioToggle());
        panel.Children.Add(CreateRadioTabs());
        panel.Children.Add(CreateRadioModes());
        panel.Children.Add(CollectionAction(_music.RepeatQueue ? "Desligar repetição da fila" : "Repetir fila", () =>
        {
            _music.RepeatQueue = !_music.RepeatQueue;
            CloseSheet();
            return Task.CompletedTask;
        }));
        var clear = CollectionAction("Limpar próximas faixas…", () =>
        {
            ShowSheet("Limpar próximas faixas", CollectionAction("Confirmar", () =>
            {
                _music.ClearQueue();
                CloseSheet();
                return Task.CompletedTask;
            }, prominent: true));
            return Task.CompletedTask;
        });
        clear.IsEnabled = _music.Upcoming.Any();
        panel.Children.Add(clear);
        ShowSheet("Opções da fila", panel);
    }

    private void OpenQueueFromPlayer()
    {
        _mobileQueueReturnPage = _page;
        ClosePlayer();
        _queueOpenedFromPlayer = _mobileCollections;
        _page = "queue";
        Refresh();
    }

    private void ReturnFromMobileQueue()
    {
        _queueOpenedFromPlayer = false;
        _page = _mobileQueueReturnPage;
        Refresh();
        ShowPlayer();
    }
}
