using System.Collections.ObjectModel;
using Avalonia.Threading;
using Hibike.Core.Models;
using Hibike.Core.Services;
namespace Hibike.App;

public sealed partial class MusicController
{
    private readonly IMusicCatalog _catalog;
    private readonly LibraryStore _store;
    private CancellationTokenSource? _search;
    private CancellationTokenSource? _play;
    private readonly SemaphoreSlim _playGate = new(1, 1);
    private readonly DispatcherTimer _timer;
    private bool _initialized;
    private int _queueIndex = -1;
    private readonly PlaybackSessionStore _session;
    private bool _restored;
    private double _resumePosition;
    private double? _seekAfterLoad;
    private DateTime _lastSave;
    private bool _radio;
    private Track? _radioSeed;
    private Task? _radioTask;
    private DateTime _radioRetryAt;
    private int _queueVersion;
    public double Position => _restored ? _resumePosition : _seekAfterLoad ?? Player.Position;
    public string DataDirectory { get; }
    public IAudioPlayer Player { get; }
    public ObservableCollection<Track> Results { get; } = [];
    public ObservableCollection<Track> Favorites { get; } = [];
    public ObservableCollection<Track> Queue { get; } = [];
    public Track? Current { get; private set; }
    public AudioSource? CurrentSource { get; private set; }
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
        _session = new PlaybackSessionStore(directory);
        LoadPersonalization();
        Queue.CollectionChanged += (_, _) => _queueVersion++;
        Changed += SaveSession;
        Player.Ended += (_, _) => Dispatcher.UIThread.Post(async () =>
        { RecordFinish(); if (Repeat && Current is not null) await PlayAsync(Current); else await NextAsync(); });
        Player.Failed += (_, message) => Dispatcher.UIThread.Post(() => Fail(message));
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) =>
        {
            RecordListening();
            if (SleepAt is { } sleep && DateTime.UtcNow >= sleep)
            { SleepAt = null; Player.Pause(); SetStatus("Sleep timer: reprodução pausada."); }
            if (_seekAfterLoad is { } seek && Player.Duration > 0)
            { Player.Seek(Math.Min(seek, Player.Duration)); _seekAfterLoad = null; }
            if ((DateTime.UtcNow - _lastSave).TotalSeconds >= 5) SaveSession();
            if (Autoplay && !_radio && _radioRetryAt != DateTime.MaxValue && Queue.Count == 1 && Current is { IsLocal: false } && !LoadingAudio && Player.IsPlaying)
            { _radio = true; _radioSeed = Current; }
            if (_radio && !LoadingAudio && Player.IsPlaying && _queueIndex + 1 >= Queue.Count && DateTime.UtcNow >= _radioRetryAt)
                _ = FillRadioAsync();
            Tick?.Invoke();
        };
        _timer.Start();
    }
    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        try
        {
            foreach (var track in await _store.LoadAsync())
                Favorites.Add(await Task.Run(() => LocalTrackMetadata.Read(track, Path.Combine(DataDirectory, "artwork"))));
            try
            {
                if (_session.Load() is { } session)
                {
                    Queue.Clear(); foreach (var item in session.Queue) Queue.Add(item);
                    History.Clear(); foreach (var item in session.History ?? []) History.Add(item);
                    Current = session.Current; _queueIndex = session.Index >= 0 && session.Index < Queue.Count ? session.Index : -1;
                    _resumePosition = session.Position; _restored = Current is not null;
                    if (_restored) Status = "Sessão restaurada. Toque para continuar.";
                }
            }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
            { PlaybackDiagnostics.Write("Sessão", ex.Message); }
            Changed?.Invoke();
        }
        catch (Exception ex) { SetStatus("Não foi possível ler a biblioteca: " + ex.Message); }
    }
    public void SetStatus(string status) { Status = status; Changed?.Invoke(); }
    private void Fail(string message)
    {
        if (_catalog is CachedMusicCatalog cached && (Pending ?? Current) is { } track) cached.Invalidate(track);
        HasError = true; PlaybackDiagnostics.Write("Erro", message); SetStatus(message);
    }
    public async Task SearchAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) { SetStatus("Digite uma música, artista ou link para buscar."); return; }
        _search?.Cancel();
        var request = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        _search = request;
        HasError = false; Searching = true; SetStatus("Buscando seu som…");
        try
        {
            var tracks = await _catalog.SearchAsync(query, request.Token);
            if (!ReferenceEquals(_search, request)) return;
            Results.Clear(); foreach (var track in tracks) Results.Add(track);
            if (_catalog is CachedMusicCatalog cached) _ = cached.WarmAsync(tracks.Take(2));
            Status = tracks.Count == 0 ? "Nenhuma faixa encontrada. Tente outro nome." : $"{tracks.Count} faixas encontradas.";
        }
        catch (OperationCanceledException) { if (ReferenceEquals(_search, request)) Status = "Busca cancelada ou tempo esgotado. Tente novamente."; }
        catch (Exception ex) { if (ReferenceEquals(_search, request)) { HasError = true; Status = ex.Message; } }
        finally
        {
            if (ReferenceEquals(_search, request)) { Searching = false; _search = null; Changed?.Invoke(); }
            request.Dispose();
        }
    }
    public void CancelSearch() => _search?.Cancel();
    public void Enqueue(Track track)
    {
        var existing = Queue.FirstOrDefault(t => t.Source == track.Source);
        if (existing is not null) { _recommended.Remove(track.Source); SetStatus("Faixa já está na fila."); return; }
        var suggested = Queue.Select((t, i) => (t, i)).FirstOrDefault(x => x.i > _queueIndex && IsRecommended(x.t));
        if (suggested.t is not null) { Queue.Insert(suggested.i, track); SetStatus("Adicionada antes das recomendações."); return; }
        Queue.Add(track); SetStatus("Adicionada à fila: " + track.Title);
    }
    public async Task PlayAsync(Track track, int? queueIndex = null)
    {
        if (queueIndex is null)
        {
            _radio = Autoplay && !track.IsLocal && (_queueIndex + 1 >= Queue.Count || Queue.Count == 1 && Queue[0].Source == track.Source);
            _radioSeed = _radio ? track : null;
            _radioRetryAt = DateTime.MinValue;
        }
        _restored = false; _seekAfterLoad = null;
        if (Current is not null && Current.Source != track.Source) RecordSkip();
        _play?.Cancel();
        var request = new CancellationTokenSource();
        _play = request;
        HasError = false; Pending = track; LoadingAudio = true; SetStatus("Obtendo áudio…");
        var stage = "YT-DLP";
        var entered = false;
        try
        {
            await _playGate.WaitAsync(request.Token); entered = true;
            if (track.IsLocal)
            {
                var original = track;
                track = await Task.Run(() => LocalTrackMetadata.Read(original, Path.Combine(DataDirectory, "artwork")), request.Token);
                request.Token.ThrowIfCancellationRequested();
                var index = Queue.IndexOf(original); if (index >= 0) Queue[index] = track;
                index = Favorites.IndexOf(original); if (index >= 0) Favorites[index] = track;
                Pending = track;
            }
            if (Player is IAudioPreparation preparing) await preparing.BeginPreparationAsync(track, request.Token);
            Player.Pause();
            PlaybackDiagnostics.Write("Reprodução", "Iniciando resolução de " + track.Id);
            using var extractTimeout = CancellationTokenSource.CreateLinkedTokenSource(request.Token);
            extractTimeout.CancelAfter(TimeSpan.FromSeconds(35));
            var source = await _catalog.ResolveAsync(track, extractTimeout.Token);
            stage = "Player";
            SetStatus("Conectando ao áudio…");
            using var playerTimeout = CancellationTokenSource.CreateLinkedTokenSource(request.Token);
            playerTimeout.CancelAfter(TimeSpan.FromSeconds(18));
            await Player.PlayAsync(source, playerTimeout.Token);
            PlaybackDiagnostics.Write("Reprodução", "Player iniciou.");
            if (queueIndex is null)
            {
                var existing = Queue.IndexOf(track);
                if (existing < 0) { Queue.Add(track); existing = Queue.Count - 1; }
                _queueIndex = existing;
            }
            else _queueIndex = queueIndex.Value;
            Current = track;
            BeginListening(track);
            var previousHistory = History.FirstOrDefault(t => t.Source == track.Source);
            if (previousHistory is not null) History.Remove(previousHistory);
            History.Insert(0, track); if (History.Count > 100) History.RemoveAt(100);
            CurrentSource = Player.PlaybackSource ?? source;
            WarmNext();
            Status = "Tocando agora";
            if (_radio && _queueIndex + 1 >= Queue.Count) _ = FillRadioAsync();
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(_play, request))
            {
                if (request.IsCancellationRequested) Status = "Reprodução cancelada.";
                else Fail(stage == "YT-DLP" ? "O YT-DLP não respondeu em 35 s. Abra o diagnóstico para ver a última etapa." : "O servidor de áudio não respondeu em 18 s. Tente outra faixa ou abra o diagnóstico.");
            }
        }
        catch (Exception ex) { if (ReferenceEquals(_play, request)) Fail(ex.Message); }
        finally
        {
            if (Player is IAudioPreparation preparing && !Player.IsPlaying) preparing.CancelPreparation();
            if (entered) _playGate.Release();
            if (ReferenceEquals(_play, request)) { _play = null; Pending = null; LoadingAudio = false; Changed?.Invoke(); }
            request.Dispose();
        }
    }
    public void CancelPlayback() => _play?.Cancel();
    public async Task ToggleAsync()
    {
        if (LoadingAudio) { CancelPlayback(); return; }
        try
        {
            if (Player.IsPlaying) Player.Pause();
            else if (Current is not null && _restored)
            {
                var position = _resumePosition;
                await PlayAsync(Current, _queueIndex >= 0 ? _queueIndex : null);
                if (!HasError)
                {
                    if (Player.Duration > 0) Player.Seek(Math.Min(position, Player.Duration));
                    else _seekAfterLoad = position;
                    SaveSession();
                }
                return;
            }
            else if (Current is not null) Player.Resume();
            else if (Queue.Count > 0) await PlayAsync(Queue[0], 0);
            else { SetStatus("Escolha uma faixa ou abra um arquivo de áudio."); return; }
            Status = Player.IsPlaying ? "Tocando agora" : "Pausado"; Changed?.Invoke();
        }
        catch (Exception ex) { SetStatus(ex.Message); }
    }
    public async Task NextAsync()
    {
        RecordSkip();
        if (_radio && !RepeatQueue && !Shuffle && _queueIndex + 1 >= Queue.Count)
            await FillRadioAsync();
        if (Queue.Count == 0) return;
        var next = Shuffle && Queue.Count > 1 ? PickRandomIndex() : _queueIndex + 1;
        if (next >= Queue.Count && RepeatQueue) next = 0;
        if (next >= Queue.Count) { Player.Pause(); SetStatus("Você chegou ao fim da fila."); return; }
        await PlayAsync(Queue[next], next);
    }
    private int PickRandomIndex()
    {
        var next = Random.Shared.Next(Queue.Count - 1);
        return next >= _queueIndex && _queueIndex >= 0 ? next + 1 : next;
    }
    public async Task PreviousAsync()
    {
        if (Player.Position > 3) { Player.Seek(0); return; }
        if (_queueIndex > 0) await PlayAsync(Queue[_queueIndex - 1], _queueIndex - 1);
    }
    public async Task ToggleFavoriteAsync(Track track)
    {
        var old = Favorites.FirstOrDefault(t => t.Id == track.Id);
        if (old is null) Favorites.Add(track); else Favorites.Remove(old);
        try { await _store.SaveAsync(Favorites); SetStatus(old is null ? "Salva na sua biblioteca." : "Removida dos favoritos."); }
        catch (Exception ex) { SetStatus("Não foi possível salvar: " + ex.Message); }
    }
    public bool IsFavorite(Track track) => Favorites.Any(t => t.Id == track.Id);
    public void ClearQueue()
    {
        _radio = false;
        _radioRetryAt = DateTime.MaxValue;
        if (Current is null) { Queue.Clear(); _queueIndex = -1; }
        else { Queue.Clear(); Queue.Add(Current); _queueIndex = 0; }
        SetStatus("Fila limpa. A faixa atual foi mantida.");
    }
    public void EditQueue(Track track, int direction)
    {
        var index = Queue.IndexOf(track); if (index < 0) return;
        if (direction == 0)
        {
            if (track == Current) { SetStatus("Faixa atual permanece na fila até terminar."); return; }
            Queue.RemoveAt(index);
        }
        else
        {
            var currentIndex = Current is null ? -1 : Queue.IndexOf(Current);
            if (index <= currentIndex) { SetStatus("Somente próximas faixas podem ser reordenadas."); return; }
            var target = Math.Clamp(index + direction, currentIndex + 1, Queue.Count - 1);
            Queue.Move(index, target);
        }
        _queueIndex = Current is null ? -1 : Queue.IndexOf(Current);
        SetStatus("Fila atualizada.");
    }
    public void EnqueueResults()
    {
        foreach (var track in Results.Where(t => !Queue.Any(q => q.Source == t.Source))) Queue.Add(track);
        SetStatus("Resultados adicionados à fila.");
    }
    public void ReplaceQueue(IEnumerable<Track> tracks)
    {
        _radio = false;
        _radioRetryAt = DateTime.MaxValue;
        var items = tracks.DistinctBy(t => t.Source).ToArray();
        Queue.Clear(); foreach (var item in items) Queue.Add(item);
        _queueIndex = Current is null ? -1 : Queue.IndexOf(Current);
        SetStatus("Fila substituída pela seleção.");
    }
    public void ReorderQueue(bool random)
    {
        var currentIndex = Current is null ? -1 : Queue.IndexOf(Current);
        var upcoming = Queue.Skip(currentIndex + 1);
        var items = Queue.Take(currentIndex + 1).Concat(random ? upcoming.OrderBy(_ => Random.Shared.Next()) : upcoming.OrderBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase)).ToArray();
        Queue.Clear(); foreach (var track in items) Queue.Add(track);
        _queueIndex = Current is null ? -1 : Queue.IndexOf(Current);
        SetStatus(random ? "Fila embaralhada." : "Fila ordenada por título.");
    }
    private Task FillRadioAsync()
    {
        if (_radioTask is { IsCompleted: false }) return _radioTask;
        return _radioTask = BuildRadioAsync();
    }
    private async Task BuildRadioAsync()
    {
        var seed = Current;
        if (!_radio || seed is null || _queueIndex + 1 < Queue.Count) return;
        var version = _queueVersion;
        RadioLoading = true; RadioStatus = "Buscando sugestões…"; Changed?.Invoke();
        _radioRetryAt = DateTime.UtcNow.AddMinutes(1);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
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
                    var matches = await _catalog.SearchAsync(anchor.Artist + " " + anchor.Title, radioTimeout.Token);
                    onlineSeed = matches.FirstOrDefault(t => SameRadioSong(anchor, t)) ?? anchor;
                }
                radio = await _catalog.RadioAsync(onlineSeed, radioTimeout.Token);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            { PlaybackDiagnostics.Write("Rádio", "Fila YouTube Music indisponível; usando busca alternativa."); }
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
                    results = await _catalog.SearchAsync(query, searchTimeout.Token);
                }
                catch (Exception error) when (error is not OutOfMemoryException)
                {
                    PlaybackDiagnostics.Write("Rádio", "Uma busca falhou; aproveitando sugestões já encontradas.");
                    if (timeout.IsCancellationRequested) break;
                    continue;
                }
                candidates.AddRange(results.Take(40).Where(Eligible).Select((track, rank) => (track,
                    (queryIndex == 0 ? 3d : 1d) + 2d / (rank + 1))));
                queryIndex++;
                if (!_radio || Current != seed || version != _queueVersion) return;
            }
            if (!_radio || Current != seed || version != _queueVersion) return;
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
            while (pool.Count > 0 && added < 10)
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
                _recommended.Add(pick.Track.Source); Queue.Add(pick.Track); added++;
            }
            PlaybackDiagnostics.Write("Rádio", $"{added} sugestões adicionadas, sem repetir fila ou histórico recente.");
            RadioStatus = added == 0 ? "Nenhuma sugestão nova. Tente outro rádio." : $"{added} recomendações · base: {anchor.Title}";
            WarmNext(); Changed?.Invoke();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { RadioStatus = "Sugestões indisponíveis. Tente novamente."; PlaybackDiagnostics.Write("Rádio", "Sugestões indisponíveis; fila preservada."); }
        finally { RadioLoading = false; Changed?.Invoke(); }
    }
    internal static string SongKey(Track track)
    {
        static string Normalize(string value) => string.Concat(value.Normalize().EnumerateRunes()
            .Where(System.Text.Rune.IsLetterOrDigit).Select(r => System.Text.Rune.ToLowerInvariant(r).ToString()));
        return Normalize(TrackTextFormatter.Artist(track.Artist)) + "\n" + Normalize(TrackTextFormatter.Title(track.Title, track.Artist));
    }
    private void WarmNext() { if (_catalog is CachedMusicCatalog cached && _queueIndex + 1 < Queue.Count) _ = cached.WarmAsync([Queue[_queueIndex + 1]]); }
    public void SaveSession()
    {
        if (!_initialized) return;
        try { _session.Save(new(Queue.ToArray(), Current, _queueIndex, Math.Max(0, Position), History.Take(100).ToArray())); SavePersonalization(); _lastSave = DateTime.UtcNow; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { PlaybackDiagnostics.Write("Salvar sessão", ex.Message); }
    }
    public void StartObserving() => _timer.Start();
    public void StopObserving() { SaveSession(); _timer.Stop(); _search?.Cancel(); }
}
