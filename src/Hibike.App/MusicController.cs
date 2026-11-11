successfully downloaded text file (SHA: fdf72bcc1590427661cafc8d7063b0c221403416)
using System.Collections.ObjectModel;
using Avalonia.Threading;
using Hibike.Core.Models;
using Hibike.Core.Services;
namespace Hibike.App;

public sealed partial class MusicController
{
    private readonly IMusicCatalog _catalog;
    private readonly LibraryStore _store;
    private readonly SemaphoreSlim _favoritesGate = new(1, 1);
    private CancellationTokenSource? _search;
    private CancellationTokenSource? _play;
    private readonly SemaphoreSlim _playGate = new(1, 1);
    private readonly DispatcherTimer _timer;
    private bool _initialized, _initializing;
    private int _queueIndex = -1;
    private readonly PlaybackSessionStore _session;
    private bool _restored;
    private double _resumePosition;
    private double? _seekAfterLoad;
    private DateTime _lastSave;
    private bool _radio;
    private Track? _radioSeed;
    private Task? _radioTask;
    private CancellationTokenSource? _radioRequest;
    private DateTime _radioRetryAt;
    private bool _advancing;
    private int _queueSession;
    public int CurrentQueueIndex => Current is null ? -1 : Queue.ToList().FindIndex(t => t.Source == Current.Source);
    public IEnumerable<Track> Upcoming => Queue.Skip(CurrentQueueIndex + 1);
    public double Position => _restored ? _resumePosition
        : _playbackRecovery is { } recovery && Current?.Source == recovery.Track.Source ? recovery.Position
        : _seekAfterLoad ?? Player.Position;
    public string DataDirectory { get; }
    public IAudioPlayer Player { get; }
    public ObservableCollection<Track> Results { get; } = [];
    public string? ResultsQuery { get; private set; }
    public string? SearchError { get; private set; }
    public ObservableCollection<Track> Favorites { get; } = [];
    public ObservableCollection<Track> Queue { get; } = [];
    public Track? Current { get; private set; }
    public AudioSource? CurrentSource { get; private set; }
    public Track? ActivePlaybackTrack { get; private set; }
    public Task<IReadOnlyList<Track>> FindPlaybackSourcesAsync(CancellationToken token) => Current is { } track
        ? PlaybackSources.FindAsync(_catalog, track, token) : Task.FromResult<IReadOnlyList<Track>>([]);
    private Track? _requestedPlaybackSource;
    public async Task SwitchPlaybackSourceAsync(Track source)
    {
        var track = Current ?? throw new InvalidOperationException("Escolha uma faixa primeiro.");
        if (source.Source != track.Source && !PlaybackSources.IsProbableMatch(track, source))
            throw new InvalidOperationException("Fonte não corresponde aos metadados da faixa.");
        _requestedPlaybackSource = source;
        try { await PlayAsync(track, _queueIndex >= 0 ? _queueIndex : null); }
        finally { _requestedPlaybackSource = null; }
        // ponytail: metadata cannot align video intros; restart at zero until audio alignment exists.
    }
    public Track? Pending { get; private set; }
    public bool HasError { get; private set; }
    public string Status { get; private set; } = "Pronto para descobrir seu próximo som.";
    public bool Searching { get; private set; }
    public bool LoadingAudio { get; private set; }
    public bool Shuffle { get; set; }
    public bool Repeat { get; set; }
    public bool RepeatQueue { get; set; }
    public DateTime? SleepAt { get; set; }
    public ObservableCollection<Track> History { get; } = [];
    public event Action? Changed;
    public event Action? Tick;

    public MusicController(IMusicCatalog catalog, IAudioPlayer player, LibraryStore store, string directory)
    {
        _catalog = catalog; Player = player; _store = store; DataDirectory = directory;
        _offlineStore = new LibraryStore(directory, OfflineFileName);
        LoadVolume();
        _session = new PlaybackSessionStore(directory);
        _stateWriter = new(WriteState, ReportSaveError);
        LoadPersonalization();
        Queue.CollectionChanged += (_, _) => _queueIndex = CurrentQueueIndex;
        Changed += SaveSession;
        Player.Ended += (_, _) =>
        {
            var generation = _playGeneration;
            Dispatcher.UIThread.Post(async () =>
            {
                if (generation != _playGeneration || LoadingAudio || _playbackRecovery is not null) return;
                RecordFinish();
                if (Repeat && Current is not null) await PlayAsync(Current, CurrentQueueIndex);
                else await NextAsync();
            });
        };
        Player.Failed += (_, message) =>
        {
            var generation = _playGeneration;
            Dispatcher.UIThread.Post(() =>
            {
                if (generation == _playGeneration && !LoadingAudio) HandlePlaybackFailure(message);
            });
        };
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) =>
        {
            ObserveConnectivity();
            RecordListening();
            if (SleepAt is { } sleep && DateTime.UtcNow >= sleep)
            { SleepAt = null; PausePlayback(); SetStatus("Sleep timer: reprodução pausada."); }
            if (_seekAfterLoad is { } seek && Player.Duration > 0)
            { Player.Seek(Math.Min(seek, Player.Duration)); _seekAfterLoad = null; }
            if ((DateTime.UtcNow - _lastSave).TotalSeconds >= 5) SaveSession();
            if (Autoplay && !_radio && _radioRetryAt != DateTime.MaxValue && Queue.Count == 1 && Current is { IsLocal: false } && !LoadingAudio && Player.IsPlaying)
            { _radio = true; _radioSeed = Current; }
            if (_radio && !LoadingAudio && Player.IsPlaying && _queueIndex + 1 >= Queue.Count && DateTime.UtcNow >= _radioRetryAt)
                _ = FillRadioAsync();
            ObservePlaybackRecovery(DateTime.UtcNow);
            Tick?.Invoke();
        };
        NetworkAvailable = AppServices.HasNetwork;
        _timer.Start();
    }
    public async Task InitializeAsync()
    {
        if (_initialized || _initializing) return;
        _initializing = true;
        try
        {
            try
            {
                foreach (var track in await _store.LoadAsync())
                    Favorites.Add(await Task.Run(() => AppServices.ReadLocalTrack(track, Path.Combine(DataDirectory, "artwork"))));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException)
            {
                PlaybackDiagnostics.WriteFailure(DiagnosticSector.Storage, "Abrir favoritos", ex);
                HasError = true; Status = "Não foi possível ler os favoritos: " + ex.Message;
            }
            try
            {
                if (_session.Load() is { } session)
                {
                    Queue.Clear(); foreach (var item in session.Queue) Queue.Add(item);
                    History.Clear(); foreach (var item in session.History ?? []) History.Add(item);
                    Current = session.Current; _queueIndex = CurrentQueueIndex;
                    _resumePosition = session.Position; _restored = Current is not null;
                    if (_restored && !HasError) Status = "Sessão restaurada. Toque para continuar.";
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException)
            { PlaybackDiagnostics.Write("Sessão", ex.Message); }
            await InitializeOfflineTracksAsync();
        }
        catch (Exception ex)
        {
            PlaybackDiagnostics.WriteFailure(DiagnosticSector.Storage, "Abrir biblioteca", ex);
            Status = "Não foi possível ler a biblioteca: " + ex.Message;
        }
        finally
        {
            // A timer tick must not overwrite the stored session while the library is loading.
            _initializing = false; _initialized = true; Changed?.Invoke();
        }
    }
    public void SetStatus(string status) { Status = status; Changed?.Invoke(); }
    private void Fail(string message)
    {
        if (_catalog is CachedMusicCatalog cached && (Pending ?? Current) is { } track) cached.Invalidate(track);
        HasError = true; PlaybackDiagnostics.Write("Erro", message); SetStatus(message);
    }
    public async Task SearchAsync(string query)
    {
        if (!CanUseOnline) return;
        if (string.IsNullOrWhiteSpace(query)) { SetStatus("Digite uma música, artista ou link para buscar."); return; }
        _search?.Cancel();
        var request = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        _search = request;
        SearchError = null;
        HasError = false; Searching = true; SetStatus("Buscando seu som…");
        try
        {
            var tracks = await _catalog.SearchAsync(query, request.Token);
            if (!ReferenceEquals(_search, request) || !CanUseOnline) return;
            Results.Clear(); foreach (var track in tracks) Results.Add(track);
            ResultsQuery = query;
            if (_catalog is CachedMusicCatalog cached) _ = cached.WarmAsync(tracks.Take(2));
            Status = tracks.Count == 0 ? "Nenhuma faixa encontrada. Tente outro nome." : $"{tracks.Count} faixas encontradas.";
        }
        catch (OperationCanceledException) { if (ReferenceEquals(_search, request)) Status = "Busca cancelada ou tempo esgotado. Tente novamente."; }
        catch (Exception ex)
        {
            if (ReferenceEquals(_search, request))
            {
                PlaybackDiagnostics.WriteFailure(DiagnosticSector.Catalog, "Busca de faixas", ex);
                SearchError = Status = ex.Message;
                HasError = true;
            }
        }
        finally
        {
            if (ReferenceEquals(_search, request)) { Searching = false; _search = null; Changed?.Invoke(); }
            request.Dispose();
        }
    }
    public void CancelSearch() => _search?.Cancel();
    public void Enqueue(Track track)
    {
        AddToQueue(track);
    }
    private void AddToQueue(Track track, bool notify = true)
    {
        if (track.IsArtist) return;
        var existing = Queue.ToList().FindIndex(t => t.Source == track.Source);
        if (existing >= 0 && existing == _queueIndex) { if (notify) SetStatus("Faixa já está tocando."); return; }
        if (existing > _queueIndex && !IsRecommended(Queue[existing])) { if (notify) SetStatus("Faixa já está na fila."); return; }
        if (existing >= 0) Queue.RemoveAt(existing);
        _recommended.Remove(track.Source);
        var suggested = Queue.Select((t, i) => (t, i)).FirstOrDefault(x => x.i > _queueIndex && IsRecommended(x.t));
        if (suggested.t is not null) Queue.Insert(suggested.i, track);
        else Queue.Add(track);
        if (notify) SetStatus("Adicionada à fila: " + track.Title);
    }
    public Task PlayAsync(Track track, int? queueIndex = null) => PlayCoreAsync(track, queueIndex, null);

    private async Task PlayCoreAsync(Track track, int? queueIndex, PlaybackRecovery? recovery)
    {
        if (track.IsArtist) { SetStatus("Abra o artista para escolher uma faixa."); return; }
        if (track.IsLocal && track.Source == _removingOfflinePath) return;
        if (!CanUseOnline && !track.IsLocal)
        {
            if (FindOfflineCopy(track) is { } local)
            {
                var existing = Queue.IndexOf(local);
                if (existing >= 0) queueIndex = existing;
                else if (queueIndex is { } index && index >= 0 && index < Queue.Count && Queue[index].Source == track.Source)
                    Queue[index] = local;
                else queueIndex = null;
                track = local;
                if (recovery is not null) recovery = recovery with { Track = local, QueueIndex = queueIndex, Source = null };
            }
            else if (OfflineMode)
            {
                SetStatus("Esta faixa ainda não está disponível offline.");
                return;
            }
        }
        var continuing = recovery is not null && !_restored && Current?.Source == track.Source;
        _playGeneration++;
        _wantsPlayback = true;
        _playbackRecovery = track.IsLocal ? null : recovery;
        _bufferingSince = null;
        if (recovery is null) _lastPlaybackPosition = 0;
        if (Current?.Source != track.Source) CancelRadioRequest();
        if (queueIndex is null && recovery is null)
        {
            _radio = Autoplay && !track.IsLocal && (_queueIndex + 1 >= Queue.Count || Queue.Count == 1 && Queue[0].Source == track.Source);
            _radioSeed = _radio ? track : null;
            _radioRetryAt = DateTime.MinValue;
        }
        _restored = false; _seekAfterLoad = null;
        if (Current is not null && Current.Source != track.Source) RecordSkip();
        _play?.Cancel();
        var request = new CancellationTokenSource();
        var requestedSource = track.IsLocal ? null : recovery?.Source ?? _requestedPlaybackSource;
        _play = request;
        HasError = false; Pending = track; LoadingAudio = true; SetStatus("Obtendo áudio…");
        var stage = "YT-DLP";
        var entered = false;
        try
        {
            PlaybackDiagnostics.Write("Reprodução", "Aguardando tentativa anterior.");
            await _playGate.WaitAsync(request.Token); entered = true;
            PlaybackDiagnostics.Write("Reprodução", "Tentativa liberada; preparando áudio.");
            if (!track.IsLocal && !CanUseOnline)
            {
                Player.Pause();
                ScheduleRecovery(recovery ?? new(track, queueIndex, requestedSource, 0));
                return;
            }
            if (track.IsLocal)
            {
                var original = track;
                track = await Task.Run(() => AppServices.ReadLocalTrack(original, Path.Combine(DataDirectory, "artwork")), request.Token);
                request.Token.ThrowIfCancellationRequested();
                var index = Queue.IndexOf(original); if (index >= 0) Queue[index] = track;
                index = Favorites.IndexOf(original); if (index >= 0) Favorites[index] = track;
                Pending = track;
            }
            if (Player is IAudioPreparation preparing)
            {
                if (continuing) await preparing.BeginRecoveryAsync(track, request.Token);
                else await preparing.BeginPreparationAsync(track, request.Token);
            }
            Player.Pause();
            PlaybackDiagnostics.Write("Reprodução", "Iniciando resolução de " + track.Id);
            using var extractTimeout = CancellationTokenSource.CreateLinkedTokenSource(request.Token);
            extractTimeout.CancelAfter(TimeSpan.FromSeconds(35));
            var resolved = requestedSource is not null
                ? (Track: requestedSource, Audio: await _catalog.ResolveAsync(requestedSource, extractTimeout.Token))
                // ponytail: automatic alternatives disabled until matching is reliable; restore ResolveFirstAsync when reactivating selector.
                : (Track: track, Audio: await _catalog.ResolveAsync(track, extractTimeout.Token));
            var source = resolved.Audio with { Title = track.Title, Artist = track.Artist, Artwork = track.Thumbnail ?? resolved.Audio.Artwork };
            stage = "Player";
            PlaybackDiagnostics.Write("Reprodução", "Áudio resolvido; enviando ao player.");
            SetStatus("Conectando ao áudio…");
            using var playerTimeout = CancellationTokenSource.CreateLinkedTokenSource(request.Token);
            playerTimeout.CancelAfter(TimeSpan.FromSeconds(18));
            await Player.PlayAsync(source, playerTimeout.Token);
            request.Token.ThrowIfCancellationRequested();
            PlaybackDiagnostics.Write("Reprodução", "Player iniciou.");
            // Queue edits may have moved the requested track while audio was resolving.
            if (!Queue.Any(t => t.Source == track.Source)) Queue.Add(track);
            Current = track;
            _queueIndex = CurrentQueueIndex;
            ActivePlaybackTrack = resolved.Track;
            if (recovery is { Position: > 0 })
            {
                if (Player.Duration > 0) Player.Seek(Math.Min(recovery.Position, Player.Duration));
                else _seekAfterLoad = recovery.Position;
                _lastPlaybackPosition = recovery.Position;
            }
            ClearRecovery();
            if (continuing) _listenTick = DateTime.UtcNow;
            else
            {
                BeginListening(track);
                var previousHistory = History.FirstOrDefault(t => t.Source == track.Source);
                if (previousHistory is not null) History.Remove(previousHistory);
                History.Insert(0, track); if (History.Count > 100) History.RemoveAt(100);
                RerankHome();
                if (OnboardingCompleted) _ = RefreshRecommendationsAsync();
            }
            var playbackSource = Player.PlaybackSource ?? source;
            CurrentSource = playbackSource with
            {
                Title = playbackSource.Title ?? source.Title ?? track.Title,
                Artist = playbackSource.Artist ?? source.Artist ?? track.Artist,
                BitrateKbps = playbackSource.BitrateKbps ?? source.BitrateKbps,
                Codec = playbackSource.Codec ?? source.Codec,
                SampleRateHz = playbackSource.SampleRateHz ?? source.SampleRateHz,
                Channels = playbackSource.Channels ?? source.Channels,
                Container = playbackSource.Container ?? source.Container,
                Format = playbackSource.Format ?? source.Format,
                BitDepth = playbackSource.BitDepth ?? source.BitDepth
            };
            WarmNext();
            Status = resolved.Track.Source == track.Source ? "Tocando agora" : "Fonte alternativa · Correspondência provável. Pode ser cover ou outra gravação.";
            if (_radio && _queueIndex + 1 >= Queue.Count) _ = FillRadioAsync();
        }
        catch (OperationCanceledException)
        {
            PlaybackDiagnostics.Write("Reprodução", "Cancelamento ou prazo excedido; etapa=" + stage);
            if (ReferenceEquals(_play, request))
            {
                if (request.IsCancellationRequested) Status = "Reprodução cancelada.";
                else if (!track.IsLocal) ScheduleRecovery(recovery ?? new(track, queueIndex, requestedSource, 0));
                else Fail(stage == "YT-DLP" ? "O YT-DLP não respondeu em 35 s. Tente novamente ou escolha outra faixa." : "O servidor de áudio não respondeu em 18 s. Tente outra faixa.");
            }
        }
        catch (Exception ex)
        {
            PlaybackDiagnostics.Write("Reprodução", "Falha; etapa=" + stage + "; " + ex.Message);
            if (ReferenceEquals(_play, request) && !request.IsCancellationRequested)
            {
                if (!track.IsLocal && (recovery is not null || !AppServices.HasNetwork || ex is HttpRequestException or IOException))
                    HandlePlaybackFailure(ex, recovery ?? new(track, queueIndex, requestedSource, 0));
                else Fail(ex.Message);
            }
        }
        finally
        {
            if (Player is IAudioPreparation preparing && !Player.IsPlaying) preparing.CancelPreparation();
            if (entered) _playGate.Release();
            if (ReferenceEquals(_play, request)) { _play = null; Pending = null; LoadingAudio = false; Changed?.Invoke(); }
            request.Dispose();
        }
    }
    public void CancelPlayback()
    {
        ClearRecovery();
        if (_play is not null || !Player.IsPlaying) _wantsPlayback = false;
        _play?.Cancel();
    }

    public Task ToggleAsync()
    {
        if (LoadingAudio) { CancelPlayback(); return Task.CompletedTask; }
        if (Player.IsPlaying) { PausePlayback(); return Task.CompletedTask; }
        return ResumePlaybackAsync();
    }
    public async Task NextAsync()
    {
        if (_advancing) return;
        if (!CanUseOnline)
        {
            var available = Upcoming.Where(CanPlayOffline).ToArray();
            if (available.Length == 0 && RepeatQueue) available = Queue.Where(CanPlayOffline).ToArray();
            if (available.Length > 0)
            {
                var track = available[Shuffle ? Random.Shared.Next(available.Length) : 0];
                _advancing = true;
                try { await PlayAsync(track, Queue.IndexOf(track)); }
                finally { _advancing = false; }
            }
            else if (_playbackRecovery is null) { PausePlayback(); SetStatus("Fim das faixas disponíveis offline na fila."); }
            return;
        }
        _advancing = true;
        ClearRecovery();
        var session = _queueSession; var current = Current;
        try
        {
            RecordSkip();
            if (_radio && !RepeatQueue && !Shuffle && !Upcoming.Any()) await FillRadioAsync();
            if (session != _queueSession || Current != current || Queue.Count == 0) return;
            var next = Shuffle && Queue.Count > 1 ? PickRandomIndex() : _queueIndex + 1;
            if (next >= Queue.Count && RepeatQueue) next = 0;
            if (next >= Queue.Count) { PausePlayback(); SetStatus("Você chegou ao fim da fila."); return; }
            await PlayAsync(Queue[next], next);
        }
        finally { _advancing = false; }
    }
    private int PickRandomIndex()
    {
        var next = Random.Shared.Next(Queue.Count - 1);
        return next >= _queueIndex && _queueIndex >= 0 ? next + 1 : next;
    }
    public async Task PreviousAsync()
    {
        if (Player.Position > 3) { Player.Seek(0); return; }
        for (var index = _queueIndex - 1; index >= 0; index--)
        {
            if (!CanUseOnline && !CanPlayOffline(Queue[index])) continue;
            await PlayAsync(Queue[index], index);
            return;
        }
    }
    public async Task ToggleFavoriteAsync(Track track)
    {
        await _favoritesGate.WaitAsync();
        try
        {
            var old = Favorites.FirstOrDefault(t => t.Id == track.Id);
            var snapshot = old is null ? Favorites.Append(track) : Favorites.Where(t => t != old);
            await _store.SaveAsync(snapshot);
            if (old is null) Favorites.Add(track); else Favorites.Remove(old);
            SetStatus(old is null ? "Salva na sua biblioteca." : "Removida dos favoritos.");
        }
        catch (Exception ex) { HasError = true; SetStatus("Não foi possível salvar: " + ex.Message); }
        finally { _favoritesGate.Release(); }
    }
    public bool IsFavorite(Track track) => Favorites.Any(t => t.Id == track.Id);
    public void ClearQueue()
    {
        CancelPlayback(); CancelRadioRequest(); _recommended.Clear(); _radioSeed = null;
        _queueSession++;
        _radio = false;
        _radioRetryAt = DateTime.MaxValue;
        RadioStatus = "Fila limpa. Busque sugestões para iniciar outro rádio.";
        if (Current is null) { Queue.Clear(); _queueIndex = -1; }
        else { Queue.Clear(); Queue.Add(Current); _queueIndex = 0; }
        SetStatus("Fila limpa. A faixa atual foi mantida.");
    }
    public void EditQueue(Track track, int direction)
    {
        var index = Queue.IndexOf(track); if (index < 0) return;
        if (direction == 0)
        {
            if (track.Source == Current?.Source) { SetStatus("Faixa atual permanece na fila até terminar."); return; }
            Queue.RemoveAt(index);
            _recommended.Remove(track.Source);
        }
        else
        {
            var currentIndex = CurrentQueueIndex;
            if (index <= currentIndex) { SetStatus("Somente próximas faixas podem ser reordenadas."); return; }
            var target = Math.Clamp(index + direction, currentIndex + 1, Queue.Count - 1);
            Queue.Move(index, target);
        }
        _queueIndex = CurrentQueueIndex;
        SetStatus("Fila atualizada.");
    }
    public void EnqueueResults()
    {
        foreach (var track in Results.DistinctBy(t => t.Source)) AddToQueue(track, notify: false);
        SetStatus("Resultados adicionados à fila.");
    }
    public void ReplaceQueue(IEnumerable<Track> tracks)
    {
        var items = tracks.Where(t => !t.IsArtist).DistinctBy(t => t.Source).ToArray();
        CancelPlayback(); CancelRadioRequest(); _recommended.Clear(); _radioSeed = null;
        _queueSession++;
        _radio = false;
        _radioRetryAt = DateTime.MaxValue;
        RadioStatus = "Sugestões aparecem quando próximas músicas acabam.";
        Queue.Clear(); foreach (var item in items) Queue.Add(item);
        _queueIndex = CurrentQueueIndex;
        SetStatus("Fila substituída pela seleção.");
    }
    public void ReorderQueue(bool random)
    {
        var currentIndex = CurrentQueueIndex;
        var upcoming = Queue.Skip(currentIndex + 1);
        var items = Queue.Take(currentIndex + 1).Concat(random ? upcoming.OrderBy(_ => Random.Shared.Next()) : upcoming.OrderBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase)).ToArray();
        Queue.Clear(); foreach (var track in items) Queue.Add(track);
        _queueIndex = CurrentQueueIndex;
        SetStatus(random ? "Fila embaralhada." : "Fila ordenada por título.");
    }
    private void CancelRadioRequest()
    {
        var request = _radioRequest;
        _radioRequest = null; _radioTask = null; RadioLoading = false; _radioRetryAt = DateTime.MinValue;
        request?.Cancel();
    }
    private Task FillRadioAsync(bool refresh = false)
    {
        if (!CanUseOnline) return Task.CompletedTask;
        if (_radioTask is { IsCompleted: false }) return _radioTask;
        if (!_radio || Current is null || !refresh && (Upcoming.Any() || DateTime.UtcNow < _radioRetryAt)) return Task.CompletedTask;
        var request = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        _radioRequest = request;
        return _radioTask = BuildRadioAsync(request, refresh);
    }
    private async Task BuildRadioAsync(CancellationTokenSource request, bool refresh)
    {
        var seed = Current!;
        bool IsActive() => ReferenceEquals(_radioRequest, request) && _radio && Current?.Source == seed.Source;
        var requestFailed = false;
        RadioLoading = true; RadioStatus = "Buscando sugestões…"; Changed?.Invoke();
        _radioRetryAt = DateTime.UtcNow.AddMinutes(1);
        try
        {
            var timeout = request;
            var anchor = _radioSeed ?? seed;
            var artists = History.OrderByDescending(Affinity).Where(t => !t.IsLocal && !string.IsNullOrWhiteSpace(t.Artist))
                .Select(t => TrackTextFormatter.Artist(t.Artist)).Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToArray();
            var queries = new[] { $"{TrackTextFormatter.Artist(anchor.Artist)} {anchor.Album} music" }
                .Concat(artists.Take(4).OrderBy(_ => Random.Shared.Next()).Take(2).Select(a => a + " músicas"));
            var candidates = new List<(Track Track, double Score)>();
            IReadOnlyList<Track> radio = [];
            try
            {
                using var radioTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                radioTimeout.CancelAfter(TimeSpan.FromSeconds(12));
                var onlineSeed = anchor;
                if (anchor.IsLocal)
                {
                    var matches = await _catalog.SearchAsync(anchor.Artist + " " + anchor.Title, radioTimeout.Token).WaitAsync(radioTimeout.Token);
                    onlineSeed = matches.FirstOrDefault(t => SameRadioSong(anchor, t)) ?? anchor;
                }
                radio = await _catalog.RadioAsync(onlineSeed, radioTimeout.Token).WaitAsync(radioTimeout.Token);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            { requestFailed = true; PlaybackDiagnostics.Write("Rádio", "Fila YouTube Music indisponível; usando busca alternativa."); }
            if (!IsActive()) return;
            var excluded = Queue.Concat(History).Append(anchor).Append(seed).ToList();
            bool Eligible(Track track) => !track.IsLocal && !IsRadioLoop(track) && !excluded.Any(t => SameRadioSong(t, track));
            candidates.AddRange(radio.Where(Eligible).Select((track, rank) => (track, 3d + 2d / (rank + 1))));
            PlaybackDiagnostics.Write("Rádio", $"YouTube Music: {radio.Count} entradas, {candidates.Count} candidatas após filtro.");
            var queryIndex = 0;
            foreach (var query in candidates.Count > 0 ? Enumerable.Empty<string>() : queries)
            {
                IReadOnlyList<Track> results;
                try
                {
                    using var searchTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                    searchTimeout.CancelAfter(TimeSpan.FromSeconds(12));
                    results = await _catalog.SearchAsync(query, searchTimeout.Token).WaitAsync(searchTimeout.Token);
                }
                catch (Exception error) when (error is not OutOfMemoryException)
                {
                    requestFailed = true;
                    PlaybackDiagnostics.Write("Rádio", "Uma busca falhou; aproveitando sugestões já encontradas.");
                    if (timeout.IsCancellationRequested) break;
                    continue;
                }
                candidates.AddRange(results.Take(40).Where(Eligible).Select((track, rank) => (track,
                    (queryIndex == 0 ? 3d : 1d) + 2d / (rank + 1))));
                queryIndex++;
                if (!IsActive()) return;
            }
            if (!IsActive()) return;
            // Re-read the queue after the awaits: manual additions/removals remain authoritative.
            excluded = Queue.Concat(History).Append(anchor).Append(seed).ToList();
            var sources = Queue.Concat(History).Select(t => t.Source).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var songs = Queue.Concat(History).Select(SongKey).ToHashSet(StringComparer.Ordinal);
            var added = 0;
            var pool = candidates.Where(c => !c.Track.IsLocal && !sources.Contains(c.Track.Source) && !songs.Contains(SongKey(c.Track)))
                .Where(c => !excluded.Any(t => SameRadioSong(t, c.Track)) && !IsRadioLoop(c.Track))
                .GroupBy(c => SongKey(c.Track)).Select(g => g.MaxBy(c => c.Score)).ToList();
            for (var i = 0; i < pool.Count; i++)
                for (var j = pool.Count - 1; j > i; j--)
                    if (SameRadioSong(pool[i].Track, pool[j].Track)) pool.RemoveAt(j);
            var artistCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var lastArtist = TrackTextFormatter.Artist(seed.Artist);
            var picks = new List<Track>();
            while (pool.Count > 0 && picks.Count < 10)
            {
                var pick = pool.MaxBy(c => c.Score + Affinity(c.Track) * .1
                    + (Favorites.Any(f => f.Artist.Equals(c.Track.Artist, StringComparison.OrdinalIgnoreCase)) ? .5 : 0)
                    + (DiscoveryMode == "Familiar" && History.Any(h => h.Artist == c.Track.Artist) ? 2 : 0)
                    - (DiscoveryMode == "Descobrir" && History.Any(h => h.Artist == c.Track.Artist) ? 2 : 0)
                    - artistCounts.GetValueOrDefault(TrackTextFormatter.Artist(c.Track.Artist)) * 2
                    - (string.Equals(lastArtist, TrackTextFormatter.Artist(c.Track.Artist), StringComparison.OrdinalIgnoreCase) ? 3 : 0));
                pool.Remove(pick);
                if (excluded.Any(t => SameRadioSong(t, pick.Track))) continue;
                if (!sources.Add(pick.Track.Source)) continue;
                excluded.Add(pick.Track);
                lastArtist = TrackTextFormatter.Artist(pick.Track.Artist);
                artistCounts[lastArtist] = artistCounts.GetValueOrDefault(lastArtist) + 1;
                picks.Add(pick.Track);
            }
            // Commit a refresh only after finding replacements; a failed request keeps the old queue.
            if (picks.Count > 0 && refresh)
                foreach (var old in Upcoming.Where(IsRecommended).ToArray())
                { Queue.Remove(old); _recommended.Remove(old.Source); }
            foreach (var track in picks) { _recommended.Add(track.Source); Queue.Add(track); added++; }
            PlaybackDiagnostics.Write("Rádio", $"{added} sugestões adicionadas, sem repetir fila ou histórico recente.");
            RadioStatus = added > 0 ? $"{added} recomendações · base: {anchor.Title}"
                : requestFailed ? "Sugestões indisponíveis. Tente novamente." : "Nenhuma sugestão nova. Tente outro rádio.";
            if (added > 0) _radioRetryAt = DateTime.MinValue;
            WarmNext(); Changed?.Invoke();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { if (IsActive()) { RadioStatus = "Sugestões indisponíveis. Tente novamente."; PlaybackDiagnostics.Write("Rádio", "Sugestões indisponíveis; fila preservada."); } }
        finally
        {
            if (ReferenceEquals(_radioRequest, request))
            { _radioRequest = null; RadioLoading = false; Changed?.Invoke(); }
            request.Dispose();
        }
    }
    internal static string SongKey(Track track)
    {
        static string Normalize(string value) => string.Concat(value.Normalize().EnumerateRunes()
            .Where(System.Text.Rune.IsLetterOrDigit).Select(r => System.Text.Rune.ToLowerInvariant(r).ToString()));
        return Normalize(TrackTextFormatter.Artist(track.Artist)) + "\n" + Normalize(TrackTextFormatter.Title(track.Title, track.Artist));
    }
    private void WarmNext() { if (CanUseOnline && _catalog is CachedMusicCatalog cached && _queueIndex + 1 < Queue.Count) _ = cached.WarmAsync([Queue[_queueIndex + 1]]); }
    public void SaveSession()
    {
        if (!_initialized || _notifyingSaveError) return;
        QueueStateSave();
    }
    public void StartObserving() => _timer.Start();
    public void StopObserving() { SaveSession(); _timer.Stop(); _search?.Cancel(); }
}
