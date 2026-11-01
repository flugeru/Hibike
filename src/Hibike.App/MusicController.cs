using System.Collections.ObjectModel;
using Avalonia.Threading;
using Hibike.Core.Models;
using Hibike.Core.Services;
namespace Hibike.App;

public sealed class MusicController
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
        Changed += SaveSession;
        Player.Ended += (_, _) => Dispatcher.UIThread.Post(async () =>
        { if (Repeat && Current is not null) await PlayAsync(Current); else await NextAsync(); });
        Player.Failed += (_, message) => Dispatcher.UIThread.Post(() => Fail(message));
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) =>
        {
            if (SleepAt is { } sleep && DateTime.UtcNow >= sleep)
            { SleepAt = null; Player.Pause(); SetStatus("Sleep timer: reprodução pausada."); }
            if (_seekAfterLoad is { } seek && Player.Duration > 0)
            { Player.Seek(Math.Min(seek, Player.Duration)); _seekAfterLoad = null; }
            if ((DateTime.UtcNow - _lastSave).TotalSeconds >= 5) SaveSession();
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
    private void Fail(string message) { HasError = true; PlaybackDiagnostics.Write("Erro", message); SetStatus(message); }
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
        Queue.Add(track); SetStatus("Adicionada à fila: " + track.Title);
    }
    public async Task PlayAsync(Track track, int? queueIndex = null)
    {
        _restored = false; _seekAfterLoad = null;
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
            var previousHistory = History.FirstOrDefault(t => t.Source == track.Source);
            if (previousHistory is not null) History.Remove(previousHistory);
            History.Insert(0, track); if (History.Count > 100) History.RemoveAt(100);
            CurrentSource = Player.PlaybackSource ?? source;
            WarmNext();
            Status = "Tocando agora";
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
        if (Current is null) { Queue.Clear(); _queueIndex = -1; }
        else { Queue.Clear(); Queue.Add(Current); _queueIndex = 0; }
        SetStatus("Fila limpa. A faixa atual foi mantida.");
    }
    public void EnqueueResults()
    {
        foreach (var track in Results.Where(t => !Queue.Any(q => q.Source == t.Source))) Queue.Add(track);
        SetStatus("Resultados adicionados à fila.");
    }
    public void ReorderQueue(bool random)
    {
        var items = random ? Queue.OrderBy(_ => Random.Shared.Next()).ToArray() : Queue.OrderBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase).ToArray();
        Queue.Clear(); foreach (var track in items) Queue.Add(track);
        _queueIndex = Current is null ? -1 : Queue.IndexOf(Current);
        SetStatus(random ? "Fila embaralhada." : "Fila ordenada por título.");
    }
    private void WarmNext() { if (_catalog is CachedMusicCatalog cached && _queueIndex + 1 < Queue.Count) _ = cached.WarmAsync([Queue[_queueIndex + 1]]); }
    public void SaveSession()
    {
        if (!_initialized) return;
        try { _session.Save(new(Queue.ToArray(), Current, _queueIndex, Math.Max(0, Position))); _lastSave = DateTime.UtcNow; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { PlaybackDiagnostics.Write("Salvar sessão", ex.Message); }
    }
    public void StartObserving() => _timer.Start();
    public void StopObserving() { SaveSession(); _timer.Stop(); _search?.Cancel(); }
}
