using Avalonia;
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
    private TextBlock _connectionTitle = null!, _connectionBody = null!, _downloadSummary = null!, _downloadEmpty = null!, _downloadQueueSummary = null!;
    private Button _connectionAction = null!, _connectionShortcut = null!, _playDownloads = null!, _clearFinishedDownloads = null!;
    private TextBox _downloadFilter = null!;
    private ItemsControl _downloadItems = null!, _downloadQueueItems = null!;
    private Track[] _shownDownloads = [];
    private string _downloadOrigin = "";
    private IReadOnlyList<Track>? _lastOfflineTracks;
    private string? _lastDownloadQuery;
    private string? _lastDownloadOrigin;
    private (bool Network, bool Offline)? _lastConnection;
    private DownloadQueue _downloadQueue = null!;
    private bool ShowOfflineHome => _page == "home" && (!_music.NetworkAvailable || _music.OfflineMode);

    private void InitializeOfflineWorkspace(Grid content, Panel status)
    {
        _connectionTitle = UiTheme.Text("", UiTheme.SectionTitle, weight: FontWeight.SemiBold);
        _connectionBody = Text("", UiTheme.Caption, Muted);
        _connectionBody.TextWrapping = TextWrapping.Wrap;
        _connectionAction = ActionButton("", async () =>
        {
            if (!_music.OfflineMode) _music.SetOfflineMode(true);
            else if (_music.NetworkAvailable)
            {
                _music.SetOfflineMode(false);
                await RefreshHomeAsync();
            }
        });
        _connectionAction.Classes.Add("primary");
        _connectionAction.HorizontalAlignment = HorizontalAlignment.Left;
        var connectionContent = new StackPanel { Spacing = UiTheme.CompactGap,
            Children = { _connectionTitle, _connectionBody, _connectionAction } };
        _connectionCard = UiTheme.Card(connectionContent);

        _connectionShortcut = ActionButton("", () =>
        {
            _page = "downloads";
            Refresh();
            return Task.CompletedTask;
        });
        _connectionBanner = new Border { Child = _connectionShortcut, IsVisible = false };
        status.Children.Add(_connectionBanner);

        _downloadFilter = UiTheme.Field("Filtrar por título, artista ou álbum");
        AddSearchIcon(_downloadFilter);
        _downloadFilter.TextChanged += (_, _) => RefreshDownloads();
        _downloadSummary = Text("", UiTheme.Caption, Muted);
        _downloadEmpty = Text("", UiTheme.Body, Muted);
        _downloadEmpty.TextWrapping = TextWrapping.Wrap;
        _playDownloads = ActionButton("Tocar todas", () => PlayCollectionAsync(_shownDownloads));
        _playDownloads.Classes.Add("primary");
        var actions = new WrapPanel { ItemSpacing = UiTheme.CompactGap, LineSpacing = UiTheme.CompactGap };
        actions.Children.Add(_playDownloads);
        actions.Children.Add(ActionButton("Importar músicas", OpenLocalAsync));
        var filters = new WrapPanel { ItemSpacing = UiTheme.CompactGap, LineSpacing = UiTheme.CompactGap };
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
        queueHeader.Children.Add(_downloadQueueSummary);
        Put(queueHeader, _clearFinishedDownloads, 0, 1);
        var activity = new StackPanel { Spacing = UiTheme.CompactGap,
            Children = { queueHeader, _downloadQueueItems } };
        _downloadActivity = UiTheme.Card(activity, padding: 12);
        _downloadQueue.Changed += RefreshDownloadQueue;
        _downloadActivity.IsVisible = false;

        _downloadItems = CollectionList<Track>(track => TrackRow(track, showMenu: true,
            openMenu: _ => ShowOfflineTrackOptions(track),
            subtitle: TrackTextFormatter.Artist(track.Artist) + " · " + _music.OfflineOrigin(track)));
        var tools = new StackPanel { Spacing = UiTheme.CompactGap,
            Children = { _connectionCard, _downloadActivity, _downloadSummary, actions, _downloadFilter, filters } };
        _downloadsPage = new Grid { IsVisible = false, RowDefinitions = new("Auto,*"), RowSpacing = UiTheme.CompactGap, ClipToBounds = true };
        var toolsScroll = UiTheme.FadingScroll(tools);
        _downloadsPage.SizeChanged += (_, e) => toolsScroll.MaxHeight = Math.Max(120, e.NewSize.Height * (_androidLayout ? .50 : .58));
        _downloadsPage.Children.Add(toolsScroll);
        Put(_downloadsPage, new Grid { Children = { UiTheme.FadingScroll(_downloadItems), _downloadEmpty } }, 1, 0);
        Put(content, _downloadsPage, 2, 0);
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
                if (hadConnection) CancelExploreSearch();
                _downloadQueue.PauseAll();
                _accountNetworkRequest?.Cancel();
            }
            _connectionTitle.Text = offline ? "Modo offline" : network ? "Músicas neste aparelho" : "Sem conexão";
            _connectionBody.Text = offline && network ? "A internet está disponível. Quer voltar ao modo online?"
                : offline ? "Você está ouvindo músicas disponíveis no aparelho."
                : network ? "Quer usar só as músicas baixadas e importadas?"
                : "As músicas deste aparelho estão disponíveis. Quer entrar no modo offline?";
            _connectionAction.Content = offline ? "Voltar ao modo online" : "Entrar no modo offline";
            _connectionAction.IsVisible = !offline || network;
            _connectionShortcut.Content = network ? "Modo offline · Ver opções" : "Sem conexão · Ver músicas offline";
            if (reconnected) Dispatcher.UIThread.Post(async () => await RefreshHomeAsync(), DispatcherPriority.Background);
        }

        var showBanner = !showDownloads && (!network && !offline || network && offline);
        if (showBanner && !_connectionBanner.IsVisible) UiMotion.Enter(_connectionBanner, -4);
        _connectionBanner.IsVisible = showBanner;
        _downloadsPage.IsVisible = showDownloads;
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
        _downloadEmpty.IsVisible = _shownDownloads.Length == 0;
        _downloadEmpty.Text = library.Count == 0
            ? "Baixe uma faixa pelo menu da música ou importe arquivos do aparelho."
            : "Nenhuma música neste filtro.";
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
        _downloadQueueSummary.Text = $"{active} ativos · {pending} na fila · {paused} pausados · {failed} com erro";
        _downloadActivity.IsVisible = jobs.Count > 0;
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
        return UiTheme.Inner(content);
    }
}
