using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Hibike.App.Downloads;
using Hibike.Core.Models;
using Hibike.Core.Services;

namespace Hibike.App.Views;

public sealed partial class MainView
{
    private Grid _downloadsPage = null!;
    private Border _connectionCard = null!, _connectionBanner = null!, _downloadActivity = null!;
    private TextBlock _connectionTitle = null!, _connectionDescription = null!, _downloadSummary = null!, _downloadQueueSummary = null!;
    private ScrollViewer _downloadEmpty = null!, _transferEmpty = null!, _transferScroll = null!;
    private Button _connectionAction = null!, _connectionShortcut = null!, _playDownloads = null!, _clearFinishedDownloads = null!;
    private Button _importDownloads = null!;
    private TextBox _downloadFilter = null!;
    private ItemsControl _downloadItems = null!, _downloadQueueItems = null!;
    private Track[] _shownDownloads = [];
    private string _downloadOrigin = "";
    private IReadOnlyList<Track>? _lastOfflineTracks;
    private string? _lastDownloadQuery;
    private string? _lastDownloadOrigin;
    private (bool Network, bool Offline)? _lastConnection;
    private DownloadQueue _downloadQueue = null!;
    private bool _showTransfers;
    private Grid _downloadLibrary = null!;
    private Button _deviceTab = null!, _transfersTab = null!;
    private bool ShowOfflineHome => _page == "home" && (!_music.NetworkAvailable || _music.OfflineMode);

    private void InitializeOfflineWorkspace(Grid content, Panel status)
    {
        _connectionTitle = UiTheme.Text("", UiTheme.Body, weight: FontWeight.SemiBold);
        _connectionDescription = UiTheme.Text("", UiTheme.Caption, Muted);
        _connectionTitle.TextWrapping = _connectionDescription.TextWrapping = TextWrapping.Wrap;
        _connectionAction = ActionButton("", async () =>
        {
            if (!_music.OfflineMode) _music.SetOfflineMode(true);
            else if (_music.NetworkAvailable)
            {
                _music.SetOfflineMode(false);
                await RefreshHomeAsync();
            }
        });
        var connectionContent = new Grid { ColumnDefinitions = new("*,Auto"), RowDefinitions = new("Auto,Auto"),
            ColumnSpacing = UiTheme.SectionGap };
        var connectionHeading = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = UiTheme.CompactGap };
        var downloadIcon = UiTheme.Chip(Icons.Make("download", UiTheme.Primary, 20));
        downloadIcon.VerticalAlignment = VerticalAlignment.Center;
        connectionHeading.Children.Add(downloadIcon);
        Put(connectionHeading, new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center,
            Children = { _connectionTitle, _connectionDescription } }, 0, 1);
        connectionContent.Children.Add(connectionHeading);
        connectionContent.Children.Add(_connectionAction);
        _connectionCard = UiTheme.Card(connectionContent, padding: 10);

        _connectionShortcut = ActionButton("", () =>
        {
            _page = "downloads";
            Refresh();
            return Task.CompletedTask;
        });
        _connectionBanner = new Border { Child = _connectionShortcut, IsVisible = false };
        status.Children.Add(_connectionBanner);

        _downloadFilter = UiTheme.Field("Buscar músicas no aparelho");
        AddSearchIcon(_downloadFilter);
        AutomationProperties.SetName(_downloadFilter, "Buscar músicas no aparelho");
        _downloadFilter.TextChanged += (_, _) => RefreshDownloads();
        _downloadSummary = Text("", UiTheme.Caption, Muted);
        _downloadEmpty = UiTheme.FadingScroll();
        _playDownloads = ActionButton("Tocar todas", () => PlayCollectionAsync(_shownDownloads));
        _playDownloads.Classes.Add("primary");
        var actions = new Grid { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = UiTheme.CompactGap };
        _downloadSummary.VerticalAlignment = VerticalAlignment.Center;
        _downloadSummary.TextWrapping = TextWrapping.Wrap;
        actions.Children.Add(new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center,
            Children = { CollectionText("Arquivos no aparelho", UiTheme.Body, weight: FontWeight.SemiBold), _downloadSummary } });
        _playDownloads.Content = Icons.Make("play", UiTheme.OnPrimary);
        ToolTip.SetTip(_playDownloads, "Tocar todas as músicas deste filtro");
        _playDownloads.MinWidth = UiTheme.TouchSize;
        AutomationProperties.SetName(_playDownloads, "Tocar todas as músicas deste filtro");
        Grid.SetColumn(_playDownloads, 1);
        actions.Children.Add(_playDownloads);
        _importDownloads = CollectionIcon("folder", "Importar arquivos", OpenLocalAsync);
        Put(actions, _importDownloads, 0, 2);
        var filters = new StackPanel { Orientation = Orientation.Horizontal, Spacing = UiTheme.CompactGap };
        foreach (var (origin, label) in new[] { ("", "Todas"), ("Download", "Baixadas"), ("Importada", "Importadas") })
        {
            var filter = ActionButton(label, () =>
            {
                _downloadOrigin = origin;
                foreach (var item in filters.Children.OfType<Button>()) item.Classes.Set("primary", Equals(item.Tag, origin));
                RefreshDownloads();
                return Task.CompletedTask;
            });
            filter.Tag = origin;
            filter.Classes.Add("pill");
            filter.Classes.Set("primary", origin.Length == 0);
            filters.Children.Add(filter);
        }

        _downloadQueue = new DownloadQueue(Path.Combine(_music.DataDirectory, "downloads"),
            _music.ResolveDownloadAsync, RegisterDownloadedTrackAsync, maxConcurrency: 2);
        _downloadQueueSummary = Text("", UiTheme.Caption, Muted);
        _downloadQueueSummary.TextWrapping = TextWrapping.Wrap;
        _downloadQueueItems = CollectionList<DownloadJob>(BuildDownloadQueueRow);
        _downloadQueueItems.ItemsSource = _downloadQueue.Jobs;
        _clearFinishedDownloads = ActionButton("Limpar concluídos", () =>
        {
            _downloadQueue.ClearFinished();
            return Task.CompletedTask;
        });
        _clearFinishedDownloads.HorizontalAlignment = HorizontalAlignment.Left;
        var queueHeader = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = UiTheme.CompactGap };
        queueHeader.Children.Add(new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center,
            Children = { CollectionText("Fila de transferências", UiTheme.Body, weight: FontWeight.SemiBold), _downloadQueueSummary } });
        Put(queueHeader, _clearFinishedDownloads, 0, 1);
        var activity = new Grid { RowDefinitions = new("Auto,*"), RowSpacing = UiTheme.CompactGap };
        activity.Children.Add(queueHeader);
        _transferScroll = UiTheme.FadingScroll(_downloadQueueItems);
        var emptyQueue = UiTheme.Card(EmptyState("download", "Nenhuma transferência",
            "Para baixar uma faixa, abra o menu da música."), padding: 0);
        emptyQueue.VerticalAlignment = VerticalAlignment.Top;
        _transferEmpty = UiTheme.FadingScroll(emptyQueue);
        Put(activity, new Grid { Children = { _transferScroll, _transferEmpty } }, 1, 0);
        _downloadActivity = new Border { Child = activity };
        _downloadQueue.Changed += RefreshDownloadQueue;
        _downloadActivity.IsVisible = false;

        _downloadItems = CollectionList<Track>(track => TrackRow(track, showMenu: true,
            openMenu: _ => ShowOfflineTrackOptions(track),
            subtitle: TrackTextFormatter.Artist(track.Artist) + " · " + _music.OfflineOrigin(track)));
        var tools = new Grid { RowDefinitions = new("Auto,Auto,Auto"), ColumnDefinitions = new("*,Auto"),
            RowSpacing = UiTheme.CompactGap, ColumnSpacing = UiTheme.SectionGap };
        tools.Children.Add(actions);
        tools.Children.Add(_downloadFilter);
        var filterRail = UiTheme.FadingScroll(filters, horizontal: true);
        tools.Children.Add(filterRail);
        Grid.SetColumnSpan(filterRail, 2);
        _downloadLibrary = new Grid { RowDefinitions = new("Auto,*"), RowSpacing = UiTheme.SectionGap };
        _downloadLibrary.Children.Add(tools);
        Put(_downloadLibrary, new Grid { Children = { UiTheme.FadingScroll(_downloadItems), _downloadEmpty } }, 1, 0);
        var tabs = new Grid { ColumnDefinitions = new("*,*"), ColumnSpacing = 2 };
        _deviceTab = ActionButton("No aparelho", () => SelectDownloadsTab(false));
        _transfersTab = ActionButton("Transferências", () => SelectDownloadsTab(true));
        foreach (var tab in new[] { _deviceTab, _transfersTab })
        {
            tab.Classes.Add("subtle");
            tab.Padding = new Thickness(4, 0);
        }
        tabs.Children.Add(_deviceTab);
        Put(tabs, _transfersTab, 0, 1);
        _downloadsPage = new Grid { IsVisible = false, RowDefinitions = new("Auto,Auto,*"), RowSpacing = UiTheme.CompactGap, ClipToBounds = true };
        _downloadsPage.Children.Add(CollectionSegments(tabs));
        Put(_downloadsPage, _connectionCard, 1, 0);
        Put(_downloadsPage, new Grid { Children = { _downloadLibrary, _downloadActivity } }, 2, 0);
        _downloadsPage.SizeChanged += (_, e) =>
        {
            var stackAction = e.NewSize.Width < 500;
            Grid.SetRow(_connectionAction, stackAction ? 1 : 0);
            Grid.SetColumn(_connectionAction, stackAction ? 0 : 1);
            Grid.SetColumnSpan(_connectionAction, stackAction ? 2 : 1);
            Grid.SetColumnSpan(connectionHeading, stackAction ? 2 : 1);
            connectionContent.RowSpacing = stackAction ? UiTheme.CompactGap : 0;

            var compactRows = e.NewSize.Width >= 600;
            Grid.SetColumn(actions, compactRows ? 1 : 0);
            Grid.SetColumnSpan(actions, compactRows ? 1 : 2);
            Grid.SetRow(_downloadFilter, compactRows ? 0 : 1);
            Grid.SetColumnSpan(_downloadFilter, compactRows ? 1 : 2);
            Grid.SetRow(filterRail, compactRows ? 1 : 2);
            tools.RowDefinitions[2].Height = compactRows ? new GridLength(0) : GridLength.Auto;
        };
        MakeTopRowsCollapsible(_downloadsPage, 2);
        MakeTopRowsCollapsible(_downloadLibrary, 1);
        MakeTopRowsCollapsible(activity, 1);
        RefreshDownloadQueue();
        Put(content, _downloadsPage, 2, 0);
    }

    private Task SelectDownloadsTab(bool showTransfers)
    {
        if (_showTransfers != showTransfers) RevealMobileTopBars();
        _showTransfers = showTransfers;
        RefreshDownloadQueue();
        return Task.CompletedTask;
    }

    private void RefreshOfflineWorkspace()
    {
        var network = _music.NetworkAvailable;
        var offline = _music.OfflineMode;
        var connectionChanged = _lastConnection != (network, offline);
        var reconnected = _lastConnection?.Network == false && network && !offline;
        var hadConnection = _lastConnection?.Network == true && _lastConnection?.Offline == false;
        var showDownloads = _page == "downloads" || ShowOfflineHome;
        if (connectionChanged)
        {
            var modeChanged = _lastConnection?.Offline != offline;
            _lastConnection = (network, offline);
            if (modeChanged) UpdateDiscordOfflineMode();
            if (!network || offline)
            {
                if (hadConnection)
                {
                    CancelExploreSearch();
                    if (_music.Current is { IsLocal: false }) _music.PausePlayback();
                }
                _downloadQueue.PauseAll(network: !network);
                _accountNetworkRequest?.Cancel();
            }
            _connectionTitle.Text = offline ? "Modo offline ativo" : network ? "Sua música, em qualquer lugar" : "Sem conexão";
            _connectionDescription.Text = offline ? "Apenas músicas salvas no aparelho."
                : network ? "Acesse suas músicas sem internet." : "Suas músicas salvas continuam disponíveis.";
            _connectionAction.Content = offline ? "Voltar ao online" : "Ativar modo offline";
            _connectionAction.IsVisible = !offline || network;
            _connectionShortcut.Content = network ? "Modo offline · Ver opções" : "Sem conexão · Ver músicas offline";
            if (reconnected)
            {
                _downloadQueue.ResumeNetworkPaused();
                Dispatcher.UIThread.Post(async () => await RefreshHomeAsync(), DispatcherPriority.Background);
            }
        }

        var showBanner = !showDownloads && (!network && !offline || network && offline);
        if (showBanner && !_connectionBanner.IsVisible) UiMotion.Enter(_connectionBanner, -4);
        _connectionBanner.IsVisible = showBanner;
        _downloadsPage.IsVisible = showDownloads;
        _connectionCard.IsVisible = !_showTransfers || !network || offline;
        if (showDownloads) RefreshDownloads();
    }

    private void RefreshDownloads()
    {
        var library = _music.OfflineTracks;
        var query = _downloadFilter.Text?.Trim() ?? "";
        if (ReferenceEquals(_lastOfflineTracks, library) && _lastDownloadQuery == query && _lastDownloadOrigin == _downloadOrigin) return;
        _lastOfflineTracks = library;
        _lastDownloadQuery = query;
        _lastDownloadOrigin = _downloadOrigin;
        var downloads = library.Count(track => _music.OfflineOrigin(track) == "Download");
        _downloadSummary.Text = $"{downloads} baixadas · {library.Count - downloads} importadas";
        _shownDownloads = library.Where(track => (_downloadOrigin.Length == 0 || _music.OfflineOrigin(track) == _downloadOrigin)
            && MatchesLibraryQuery($"{track.Title} {track.Artist} {track.Album}", query)).Reverse().ToArray();
        _downloadItems.ItemsSource = _shownDownloads;
        _playDownloads.IsEnabled = _shownDownloads.Length > 0;
        _playDownloads.IsVisible = _shownDownloads.Length > 0;
        _importDownloads.IsVisible = library.Count > 0;
        _downloadEmpty.IsVisible = _shownDownloads.Length == 0;
        if (_shownDownloads.Length == 0 && !Equals(_downloadEmpty.Tag, library.Count == 0))
        {
            _downloadEmpty.Tag = library.Count == 0;
            var empty = library.Count == 0
                ? EmptyState("download", "Sua coleção começa aqui", "Baixe uma faixa pelo menu da música ou importe arquivos do aparelho.",
                    "Importar arquivos", OpenLocalAsync)
                : EmptyState("search", "Nenhuma música encontrada", "Tente outra busca ou mude o filtro.");
            var card = UiTheme.Card(empty, padding: 0);
            card.VerticalAlignment = VerticalAlignment.Top;
            _downloadEmpty.Content = card;
        }
    }

    private void ShowOfflineTrackOptions(Track track)
    {
        var details = new StackPanel { Spacing = UiTheme.CompactGap };
        string size;
        try { size = $"{new FileInfo(track.Source).Length / 1048576d:0.0} MB"; }
        catch (IOException) { size = "Arquivo indisponível"; }
        catch (UnauthorizedAccessException) { size = "Arquivo indisponível"; }
        details.Children.Add(Text(_music.OfflineOrigin(track) + " · " + size, UiTheme.Caption, Muted));
        details.Children.Add(ActionButton("Tocar a seguir", () => { _music.PlayNext(track); CloseSheet(); return Task.CompletedTask; }));
        details.Children.Add(ActionButton("Adicionar à fila", () => { _music.Enqueue(track); CloseSheet(); return Task.CompletedTask; }));
        var remove = ActionButton("Remover arquivo do aparelho", () =>
        {
            var confirmation = new StackPanel { Spacing = UiTheme.CompactGap };
            var message = Text(_music.Current?.Source == track.Source
                ? "A reprodução será encerrada e a faixa será removida do Hibike."
                : "A faixa será removida dos Downloads, da fila e das playlists do Hibike.", UiTheme.Body);
            message.TextWrapping = TextWrapping.Wrap;
            confirmation.Children.Add(message);
            confirmation.Children.Add(ActionButton("Confirmar remoção", async () =>
            {
                await _music.RemoveOfflineTrackAsync(track);
                CloseSheet();
            }));
            ShowSheet("Remover " + track.Title + "?", confirmation);
            return Task.CompletedTask;
        });
        remove.IsEnabled = _music.CanRemoveOfflineTrack(track);
        details.Children.Add(remove);
        if (!remove.IsEnabled) details.Children.Add(Text("Aguarde a preparação do áudio para remover este arquivo.", UiTheme.Caption, Muted));
        ShowSheet(track.Title, details);
    }

    private Task DownloadCurrentAsync() => _music.Current is { } track ? DownloadTrackAsync(track) : Task.CompletedTask;

    private Task DownloadTrackAsync(Track track)
    {
        CloseSheet();
        ClosePlayer();
        _page = "downloads";
        Refresh();
        if (!_music.CanUseOnline || track.IsLocal || track.IsArtist) return Task.CompletedTask;
        if (_music.OfflineTracks.Any(local => local.DownloadedFrom == track.Source && File.Exists(local.Source)))
        {
            _downloadQueueSummary.Text = "Esta faixa já está no aparelho.";
            return Task.CompletedTask;
        }
        _downloadQueue.Enqueue(track);
        RevealMobileTopBars();
        _showTransfers = true;
        RefreshDownloadQueue();
        return Task.CompletedTask;
    }

    private async Task RegisterDownloadedTrackAsync(Track track, AudioSource source, string path, CancellationToken token)
    {
        var thumbnail = await ArtworkCache.SaveOfflineCoverAsync(track.Thumbnail ?? source.Artwork, path, token);
        token.ThrowIfCancellationRequested();
        await _music.SaveOfflineTrackAsync(track with
        {
            Id = Guid.NewGuid().ToString("N"),
            Source = path, IsLocal = true, DownloadedFrom = track.Source, Thumbnail = thumbnail
        });
    }

    private void RefreshDownloadQueue()
    {
        if (_downloadQueue is null) return;
        var jobs = _downloadQueue.Jobs;
        var active = jobs.Count(job => job.Stage is TransferStage.Resolving or TransferStage.Transferring or TransferStage.Finalizing);
        var pending = jobs.Count(job => job.Stage == TransferStage.Queued);
        var paused = jobs.Count(job => job.Stage == TransferStage.Paused);
        var failed = jobs.Count(job => job.Stage == TransferStage.Failed);
        _downloadQueueSummary.IsVisible = jobs.Count > 0;
        _downloadQueueSummary.Text = $"{active} em andamento · {pending} na fila · {paused} pausadas · {failed} com erro";
        _transferScroll.IsVisible = jobs.Count > 0;
        _transferEmpty.IsVisible = jobs.Count == 0;
        _transfersTab.Content = active + pending > 0 ? $"Transferências ({active + pending})" : "Transferências";
        _deviceTab.Classes.Set("primary", !_showTransfers);
        _transfersTab.Classes.Set("primary", _showTransfers);
        var entering = _showTransfers ? !_downloadActivity.IsVisible : !_downloadLibrary.IsVisible;
        _downloadActivity.IsVisible = _showTransfers;
        _downloadLibrary.IsVisible = !_showTransfers;
        if (entering && _downloadsPage.IsEffectivelyVisible) Animate(_showTransfers ? _downloadActivity : _downloadLibrary, 6);
        _connectionCard.IsVisible = !_showTransfers || !_music.NetworkAvailable || _music.OfflineMode;
        _clearFinishedDownloads.IsVisible = jobs.Any(job => job.Stage is TransferStage.Completed or TransferStage.Cancelled);
    }

    private Control BuildDownloadQueueRow(DownloadJob job)
    {
        var header = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = UiTheme.CompactGap };
        header.Children.Add(new Artwork(job.Track, UiTheme.TouchSize));
        var labels = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var title = Text(TrackTextFormatter.Title(job.Track.Title, job.Track.Artist), UiTheme.Body);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.MaxLines = 1;
        labels.Children.Add(title);
        var artist = Text(TrackTextFormatter.Artist(job.Track.Artist), UiTheme.Caption, Muted);
        artist.TextTrimming = TextTrimming.CharacterEllipsis;
        artist.MaxLines = 1;
        labels.Children.Add(artist);
        Put(header, labels, 0, 1);

        var status = Text("", UiTheme.Caption, Muted);
        status.TextWrapping = TextWrapping.Wrap;
        status.MaxLines = 3;
        status.Bind(TextBlock.TextProperty, new Binding(nameof(DownloadJob.StatusText)) { Source = job });
        var progress = new ProgressBar { Maximum = 1, Height = UiTheme.ProgressHeight };
        progress.Bind(ProgressBar.ValueProperty, new Binding(nameof(DownloadJob.Progress)) { Source = job });
        progress.Bind(ProgressBar.IsIndeterminateProperty, new Binding(nameof(DownloadJob.IsIndeterminate)) { Source = job });
        progress.Bind(Visual.IsVisibleProperty, new Binding(nameof(DownloadJob.Stage))
        {
            Source = job,
            Converter = new Avalonia.Data.Converters.FuncValueConverter<TransferStage, bool>(stage =>
                stage is TransferStage.Transferring or TransferStage.Paused or TransferStage.Finalizing)
        });
        var actions = new WrapPanel { ItemSpacing = UiTheme.CompactGap, LineSpacing = UiTheme.CompactGap };
        var pause = ActionButton("Pausar", () => { _downloadQueue.Pause(job); return Task.CompletedTask; });
        pause.Bind(Visual.IsVisibleProperty, new Binding(nameof(DownloadJob.CanPause)) { Source = job });
        var resume = ActionButton("Retomar / Repetir", () => { _downloadQueue.Resume(job); return Task.CompletedTask; });
        resume.Bind(Visual.IsVisibleProperty, new Binding(nameof(DownloadJob.CanResume)) { Source = job });
        var cancel = ActionButton("Cancelar", () => { _downloadQueue.Cancel(job); return Task.CompletedTask; });
        cancel.Bind(Visual.IsVisibleProperty, new Binding(nameof(DownloadJob.CanCancel)) { Source = job });
        actions.Children.Add(pause);
        actions.Children.Add(resume);
        actions.Children.Add(cancel);
        var content = new StackPanel { Spacing = UiTheme.CompactGap,
            Children = { header, status, progress, actions } };
        var card = UiTheme.Card(content, padding: 12);
        card.Margin = new Thickness(0, 0, 0, UiTheme.CompactGap);
        return card;
    }
}
