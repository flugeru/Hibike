using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Hibike.Core.Models;
using Hibike.Core.Services;

namespace Hibike.App.Views;

public sealed partial class MainView
{
    private Grid _downloadsPage = null!;
    private Border _connectionCard = null!, _connectionBanner = null!, _downloadActivity = null!;
    private TextBlock _connectionTitle = null!, _connectionBody = null!, _downloadSummary = null!, _downloadEmpty = null!, _downloadStatus = null!;
    private Button _connectionAction = null!, _connectionShortcut = null!, _playDownloads = null!, _downloadAction = null!;
    private ProgressBar _downloadProgress = null!;
    private TextBox _downloadFilter = null!;
    private ItemsControl _downloadItems = null!;
    private Track[] _shownDownloads = [];
    private string _downloadOrigin = "";
    private IReadOnlyList<Track>? _lastOfflineTracks;
    private string? _lastDownloadQuery;
    private string? _lastDownloadOrigin;
    private (bool Network, bool Offline)? _lastConnection;
    private CancellationTokenSource? _download;
    private Track? _retryDownload;
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

        _downloadStatus = Text("", UiTheme.Caption, Muted);
        _downloadStatus.TextWrapping = TextWrapping.Wrap;
        _downloadProgress = new ProgressBar { Maximum = 1, Height = 4, IsVisible = false };
        _downloadAction = ActionButton("Cancelar", () =>
        {
            if (_download is { } active) active.Cancel();
            else if (_retryDownload is { } retry) return DownloadTrackAsync(retry);
            return Task.CompletedTask;
        });
        var activity = new Grid { ColumnDefinitions = new("*,Auto"), RowDefinitions = new("Auto,Auto"), ColumnSpacing = UiTheme.CompactGap };
        activity.Children.Add(_downloadStatus);
        Put(activity, _downloadAction, 0, 1);
        Put(activity, _downloadProgress, 1, 0);
        Grid.SetColumnSpan(_downloadProgress, 2);
        _downloadActivity = UiTheme.Card(activity);
        _downloadActivity.IsVisible = false;

        _downloadItems = CollectionList<Track>(track => TrackRow(track, showMenu: true,
            openMenu: _ => ShowOfflineTrackOptions(track),
            subtitle: TrackTextFormatter.Artist(track.Artist) + " · " + _music.OfflineOrigin(track)));
        var tools = new StackPanel { Spacing = UiTheme.CompactGap,
            Children = { _connectionCard, _downloadActivity, _downloadSummary, actions, _downloadFilter, filters } };
        _downloadsPage = new Grid { IsVisible = false, RowDefinitions = new("Auto,*"), RowSpacing = UiTheme.CompactGap, ClipToBounds = true };
        var toolsScroll = UiTheme.FadingScroll(tools);
        _downloadsPage.SizeChanged += (_, e) => toolsScroll.MaxHeight = Math.Max(64, e.NewSize.Height * .64);
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
                _download?.Cancel();
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

        _downloadAction.IsEnabled = _download is not null || _music.CanUseOnline;
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

    private async Task DownloadTrackAsync(Track track)
    {
        CloseSheet();
        ClosePlayer();
        _page = "downloads";
        Refresh();
        if (_download is not null) return;
        if (!_music.CanUseOnline || track.IsLocal || track.IsArtist) return;
        if (_music.OfflineTracks.Any(local => local.DownloadedFrom == track.Source && File.Exists(local.Source)))
        {
            SetDownloadStatus("Esta faixa já está no aparelho.");
            return;
        }

        using var request = new CancellationTokenSource(TimeSpan.FromMinutes(30));
        _download = request;
        _retryDownload = null;
        SetDownloadStatus("Preparando download de " + track.Title + "…");
        string? path = null;
        try
        {
            using var resolve = CancellationTokenSource.CreateLinkedTokenSource(request.Token);
            resolve.CancelAfter(TimeSpan.FromSeconds(35));
            var source = await _music.ResolveDownloadAsync(track, resolve.Token);
            var progress = new Progress<AudioDownloadProgress>(update =>
            {
                if (_download != request || request.IsCancellationRequested) return;
                var percent = update.TotalBytes is > 0 ? (double?)update.Bytes / update.TotalBytes : null;
                SetDownloadStatus($"Baixando {track.Title} · {update.Bytes / 1048576d:0.0} MB", percent);
            });
            path = await AudioDownload.SaveAsync(source, Path.Combine(_music.DataDirectory, "downloads"), progress, request.Token);
            SetDownloadStatus("Salvando no aparelho…", 1);
            var thumbnail = await ArtworkCache.SaveOfflineCoverAsync(track.Thumbnail ?? source.Artwork, path, request.Token);
            await _music.SaveOfflineTrackAsync(track with
            {
                Id = Guid.NewGuid().ToString("N"), Source = path, IsLocal = true, DownloadedFrom = track.Source, Thumbnail = thumbnail
            });
            path = null;
            FinishDownload("Download concluído.");
        }
        catch (OperationCanceledException)
        {
            _retryDownload = track;
            FinishDownload(!_music.CanUseOnline ? "Download interrompido. Tente quando estiver online."
                : request.IsCancellationRequested ? "Download cancelado ou tempo esgotado." : "A faixa demorou para responder. Tente baixar de novo.");
        }
        catch (Exception error)
        {
            _retryDownload = track;
            PlaybackDiagnostics.WriteFailure(DiagnosticSector.Storage, "Baixar faixa", error);
            FinishDownload("Não foi possível baixar esta faixa. Tente novamente.");
        }
        finally
        {
            _download = null;
            if (path is not null)
            {
                try { File.Delete(path); File.Delete(Path.ChangeExtension(path, ".cover")); }
                catch (IOException error) { PlaybackDiagnostics.WriteFailure(DiagnosticSector.Storage, "Limpar download", error); }
                catch (UnauthorizedAccessException error) { PlaybackDiagnostics.WriteFailure(DiagnosticSector.Storage, "Limpar download", error); }
            }
        }

        void FinishDownload(string message)
        {
            _download = null;
            SetDownloadStatus(message);
        }
    }

    private void SetDownloadStatus(string status, double? progress = null)
    {
        var opening = !_downloadActivity.IsVisible;
        _downloadActivity.IsVisible = true;
        _downloadStatus.Text = status;
        var active = _download is not null;
        _downloadAction.IsVisible = active || _retryDownload is not null;
        _downloadAction.IsEnabled = active || _music.CanUseOnline;
        _downloadAction.Content = active ? "Cancelar" : "Tentar novamente";
        _downloadProgress.IsVisible = active;
        _downloadProgress.IsIndeterminate = active && progress is null && !_music.ReduceMotion;
        _downloadProgress.Value = progress ?? .5;
        if (opening) UiMotion.Enter(_downloadActivity, 4);
    }
}
