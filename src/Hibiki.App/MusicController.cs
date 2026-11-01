using System.Collections.ObjectModel;
using Avalonia.Threading;
using Hibiki.Core.Models;
using Hibiki.Core.Services;
namespace Hibiki.App;

public sealed class MusicController
{
    private readonly IMusicCatalog _catalog;
    private readonly LibraryStore _store;
    private CancellationTokenSource? _search;
    private CancellationTokenSource? _play;
    private readonly SemaphoreSlim _playGate = new(1, 1);
    private readonly DispatcherTimer _timer;
    private int _queueIndex = -1;
    public string DataDirectory { get; }
    public IAudioPlayer Player { get; }
    public ObservableCollection<Track> Results { get; } = [];
    public ObservableCollection<Track> Favorites { get; } = [];
    public ObservableCollection<Track> Queue { get; } = [];
    public Track? Current { get; private set; }
    public string Status { get; private set; } = "Pronto para descobrir seu próximo som.";
    public bool Searching { get; private set; }
    public bool LoadingAudio { get; private set; }
    public bool Shuffle { get; set; }
    public bool Repeat { get; set; }
    public event Action? Changed;
    public event Action? Tick;

    public MusicController(IMusicCatalog catalog, IAudioPlayer player, LibraryStore store, string directory)
    {
        _catalog = catalog; Player = player; _store = store; DataDirectory = directory;
        Player.Ended += (_, _) => Dispatcher.UIThread.Post(async () =>
        { if (Repeat && Current is not null) await PlayAsync(Current); else await NextAsync(); });
        Player.Failed += (_, message) => Dispatcher.UIThread.Post(() => SetStatus(message));
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => Tick?.Invoke();
        _timer.Start();
    }
    public async Task InitializeAsync()
    {
        try { foreach (var track in await _store.LoadAsync()) Favorites.Add(track); Changed?.Invoke(); }
        catch (Exception ex) { SetStatus("Não foi possível ler a biblioteca: " + ex.Message); }
    }
    public void SetStatus(string status) { Status = status; Changed?.Invoke(); }
    public async Task SearchAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) { SetStatus("Digite uma música, artista ou link para buscar."); return; }
        _search?.Cancel();
        var request = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        _search = request;
        Searching = true; SetStatus("Buscando seu som…");
        try
        {
            var tracks = await _catalog.SearchAsync(query, request.Token);
            if (!ReferenceEquals(_search, request)) return;
            Results.Clear(); foreach (var track in tracks) Results.Add(track);
            Status = tracks.Count == 0 ? "Nenhuma faixa encontrada. Tente outro nome." : $"{tracks.Count} faixas encontradas.";
        }
        catch (OperationCanceledException) { if (ReferenceEquals(_search, request)) Status = "Busca cancelada ou tempo esgotado. Tente novamente."; }
        catch (Exception ex) { if (ReferenceEquals(_search, request)) Status = ex.Message; }
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
        _play?.Cancel();
        var request = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        _play = request;
        LoadingAudio = true; SetStatus("Preparando: " + track.Title);
        var entered = false;
        try
        {
            await _playGate.WaitAsync(request.Token); entered = true;
            Player.Pause();
            var source = await _catalog.ResolveAsync(track, request.Token);
            await Player.PlayAsync(source, request.Token);
            if (queueIndex is null)
            {
                var existing = Queue.IndexOf(track);
                if (existing < 0) { Queue.Add(track); existing = Queue.Count - 1; }
                _queueIndex = existing;
            }
            else _queueIndex = queueIndex.Value;
            Current = track;
            Status = "Tocando agora";
        }
        catch (OperationCanceledException) { if (ReferenceEquals(_play, request)) Status = "Reprodução cancelada ou tempo esgotado."; }
        catch (Exception ex) { if (ReferenceEquals(_play, request)) Status = ex.Message; }
        finally
        {
            if (entered) _playGate.Release();
            if (ReferenceEquals(_play, request)) { _play = null; LoadingAudio = false; Changed?.Invoke(); }
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
    public void StopObserving() { _timer.Stop(); _search?.Cancel(); _play?.Cancel(); Player.Pause(); }
}
