using System.Text.Json;
using Hibike.Core.Models;
using Hibike.Core.Services;

namespace Hibike.App;

public sealed partial class MusicController
{
    private static bool IsRadioLoop(Track track) => System.Text.RegularExpressions.Regex.IsMatch(track.Title,
        @"\b(extended|loop|slowed|reverb|sped\s*up|nightcore|\d+\s*(hours?|hrs?|horas?))\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    internal static bool SameRadioSong(Track left, Track right)
    {
        static HashSet<string> Words(Track track)
        {
            var title = track.MusicTitle ?? TrackTextFormatter.Title(track.Title, track.Artist);
            title = System.Text.RegularExpressions.Regex.Replace(title, @"\([^)]*\)", " ");
            title = System.Text.RegularExpressions.Regex.Replace(title.Normalize(System.Text.NormalizationForm.FormKD).ToLowerInvariant(), @"\p{Mn}", "");
            return System.Text.RegularExpressions.Regex.Matches(title, @"[\p{L}\p{N}]+")
                .Select(m => m.Value).Where(w => (w.Length > 1 || int.TryParse(w, out _))
                    && w is not ("official" or "audio" or "video" or "mv" or "pv" or "full" or "lyrics" or "lyric" or "ost" or "soundtrack" or "extended" or "cover" or "music" or "original" or "piano" or "instrumental" or "remaster" or "remastered"))
                .ToHashSet(StringComparer.Ordinal);
        }
        if (left.Source.Equals(right.Source, StringComparison.OrdinalIgnoreCase)) return true;
        if (left.EquivalentVideoIds?.Contains(right.Id) == true || right.EquivalentVideoIds?.Contains(left.Id) == true
            || left.EquivalentVideoIds is { } ids && right.EquivalentVideoIds is { } others && ids.Intersect(others).Any()) return true;
        var a = Words(left); var b = Words(right);
        if (a.Count == 0 || b.Count == 0) return false;
        static string Artist(Track t) => System.Text.RegularExpressions.Regex.Replace(
            (t.MusicArtist ?? TrackTextFormatter.Artist(t.Artist)).Normalize(System.Text.NormalizationForm.FormKC).ToLowerInvariant(), @"[^\p{L}\p{N}]", "");
        var sameArtist = Artist(left).Length > 0 && Artist(left) == Artist(right);
        var durationClose = left.DurationSeconds > 0 && right.DurationSeconds > 0
            && Math.Abs(left.DurationSeconds - right.DurationSeconds) <= Math.Max(12, Math.Min(left.DurationSeconds, right.DurationSeconds) * .06);
        var structuredConflict = left.MusicArtist is not null && right.MusicArtist is not null && !sameArtist;
        if (structuredConflict) return false;
        // Short names need artist evidence or title containment plus duration; duration alone never identifies a song.
        if (Math.Min(a.Count, b.Count) <= 2)
        {
            if (!a.IsSubsetOf(b) && !b.IsSubsetOf(a)) return false;
            if (sameArtist) return true;
            if (!durationClose) return false;
            if (!a.SetEquals(b)) return true;
            if (System.Text.RegularExpressions.Regex.IsMatch(left.Title + " " + right.Title, @"\b(official|mv|lyrics?|ending)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return true;
            return Artist(left).Length == 0 || Artist(right).Length == 0
                || left.MusicArtist is not null && right.MusicArtist is null
                || right.MusicArtist is not null && left.MusicArtist is null;
        }
        var overlap = a.Intersect(b).Count();
        // ponytail: metadata fallback cannot recognize fully translated titles; use recording IDs/fingerprints when available.
        return a.SetEquals(b) && (sameArtist || durationClose) || Math.Min(a.Count, b.Count) >= 3 && overlap >= Math.Ceiling(Math.Min(a.Count, b.Count) * .8)
            && (a.IsSubsetOf(b) || b.IsSubsetOf(a) || overlap >= Math.Ceiling(Math.Max(a.Count, b.Count) * .75));
    }
    public sealed class ListeningStat
    {
        public double Seconds { get; set; }
        public int Plays { get; set; }
        public int Completed { get; set; }
        public int Skips { get; set; }
        public DateTime LastPlayed { get; set; }
    }
    public sealed class LocalPlaylist
    {
        public string Name { get; set; } = "";
        public bool Pinned { get; set; }
        public List<Track> Tracks { get; set; } = [];
    }
    public sealed class Personalization
    {
        public Dictionary<string, ListeningStat> Listening { get; set; } = [];
        public List<Track> Pins { get; set; } = [];
        public List<LocalPlaylist> Playlists { get; set; } = [];
        public bool Autoplay { get; set; } = true;
        public string Discovery { get; set; } = "Tudo";
        public bool OnboardingCompleted { get; set; }
        public List<MusicArtist> Artists { get; set; } = [];
        public List<string> Genres { get; set; } = [];
        public HashSet<string> Dismissed { get; set; } = [];
        public string Region { get; set; } = "BR";
        public RecommendationWeights Weights { get; set; } = new();
    }
    private Personalization _personal = new();
    private readonly HashSet<string> _recommended = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _listenTick = DateTime.UtcNow;
    private double _sessionSeconds;
    private bool _finished;
    public IReadOnlyList<LocalPlaylist> Playlists => _personal.Playlists;
    public IReadOnlyList<Track> Pins => _personal.Pins;
    public IReadOnlyDictionary<string, ListeningStat> Listening => _personal.Listening;
    public bool Autoplay => _personal.Autoplay;
    public string DiscoveryMode => _personal.Discovery;
    public bool RadioLoading { get; private set; }
    public string RadioStatus { get; private set; } = "Sugestões aparecem quando próximas músicas acabam.";
    public string PlaybackContext => _radioSeed is { } seed && _radio ? "Rádio de " + seed.Title : SelectionContext;
    public string SelectionContext { get; set; } = "Sua seleção";
    public bool IsRecommended(Track track) => _recommended.Contains(track.Source);
    public double Affinity(Track track) => _personal.Listening.TryGetValue(SongKey(track), out var stat)
        ? stat.Completed * 2 + Math.Log(1 + stat.Seconds / 60) - stat.Skips : 0;
    private void LoadPersonalization()
    {
        try
        {
            var path = Path.Combine(DataDirectory, "personalization.json");
            if (!File.Exists(path)) return;
            var data = JsonSerializer.Deserialize<Personalization>(File.ReadAllText(path));
            if (data is null || data.Listening is null || data.Pins is null || data.Playlists is null
                || data.Artists is null || data.Genres is null || data.Dismissed is null || data.Weights is null
                || data.Artists.Count > 20 || data.Artists.Any(a => a is null || string.IsNullOrWhiteSpace(a.Name) || !System.Text.RegularExpressions.Regex.IsMatch(a.Id ?? "", "^UC[\\w-]{22}$"))
                || data.Genres.Any(g => string.IsNullOrWhiteSpace(g) || g.Length > 60)
                || !System.Text.RegularExpressions.Regex.IsMatch(data.Region ?? "", "^[A-Z]{2}$")
                || data.Pins.Any(t => t is null || string.IsNullOrWhiteSpace(t.Source))
                || data.Playlists.Any(p => p is null || string.IsNullOrWhiteSpace(p.Name) || p.Tracks is null || p.Tracks.Any(t => t is null || string.IsNullOrWhiteSpace(t.Source)))
                || data.Listening.Any(p => p.Value is null || !double.IsFinite(p.Value.Seconds) || p.Value.Seconds < 0))
                throw new InvalidDataException("Dados de personalização inválidos.");
            _personal = data;
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        { PlaybackDiagnostics.Write("Personalização", "Não foi possível carregar dados locais: " + error.Message); }
    }
    private void SavePersonalization() => QueueStateSave();
    private void PersistPersonalization()
    {
        if (!_initialized) QueueStateSave();
        Changed?.Invoke();
    }
    private void BeginListening(Track track)
    {
        _sessionSeconds = 0; _finished = false; _listenTick = DateTime.UtcNow;
        var key = SongKey(track);
        if (!_personal.Listening.TryGetValue(key, out var stat)) _personal.Listening[key] = stat = new();
        stat.Plays++; stat.LastPlayed = DateTime.UtcNow;
    }
    private void RecordListening()
    {
        var now = DateTime.UtcNow; var elapsed = Math.Clamp((now - _listenTick).TotalSeconds, 0, 2); _listenTick = now;
        if (Current is null || LoadingAudio || !Player.IsPlaying || _finished) return;
        if (!_personal.Listening.TryGetValue(SongKey(Current), out var stat)) return;
        stat.Seconds += elapsed; _sessionSeconds += elapsed;
    }
    private void RecordFinish()
    {
        if (_finished || Current is null) return;
        _finished = true;
        if (_personal.Listening.TryGetValue(SongKey(Current), out var stat)) stat.Completed++;
    }
    private void RecordSkip()
    {
        if (_finished || Current is null) return;
        _finished = true;
        if (_sessionSeconds < 30 && _personal.Listening.TryGetValue(SongKey(Current), out var stat)) stat.Skips++;
    }
    public void SetAutoplay(bool enabled)
    {
        CancelRadioRequest();
        _personal.Autoplay = enabled; _radio = enabled;
        _radioRetryAt = enabled ? DateTime.MinValue : DateTime.MaxValue;
        if (enabled) _radioSeed ??= Current;
        PersistPersonalization();
        if (enabled) _ = FillRadioAsync();
    }
    public void SetDiscovery(string mode)
    {
        if (mode is not ("Tudo" or "Familiar" or "Descobrir")) throw new ArgumentException("Modo inválido.");
        CancelRadioRequest();
        _personal.Discovery = mode; PersistPersonalization();
    }
    public Task RetryRadioAsync()
    {
        if (Current is null)
        { RadioStatus = "Escolha uma música para buscar sugestões."; Changed?.Invoke(); return Task.CompletedTask; }
        if (RadioLoading) return _radioTask ?? Task.CompletedTask;
        _personal.Autoplay = true;
        _radio = true; _radioSeed ??= Current; _radioRetryAt = DateTime.MinValue;
        PersistPersonalization();
        return FillRadioAsync(refresh: true);
    }
    public async Task StartRadioAsync(Track track)
    {
        ReplaceQueue([track]); _personal.Autoplay = true; _radio = true; _radioSeed = track;
        _radioRetryAt = DateTime.MinValue;
        await PlayAsync(track, 0);
        if (!HasError && _radio && _radioSeed?.Source == track.Source && Current?.Source == track.Source) await FillRadioAsync();
    }
    public void PlayNext(Track track)
    {
        if (track.IsArtist) return;
        var existing = Queue.ToList().FindIndex(t => t.Source == track.Source);
        if (track.Source == Current?.Source) return;
        if (existing >= 0) Queue.RemoveAt(existing);
        _recommended.Remove(track.Source);
        _queueIndex = CurrentQueueIndex;
        Queue.Insert(_queueIndex + 1, track); SetStatus("Tocará a seguir: " + track.Title);
    }
    public void TogglePin(Track track)
    {
        var existing = _personal.Pins.FirstOrDefault(t => t.Source == track.Source);
        if (existing is null) _personal.Pins.Add(track); else _personal.Pins.Remove(existing);
        PersistPersonalization();
    }
    public void CreatePlaylist(string name)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 100 || _personal.Playlists.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Nome vazio, longo demais ou já utilizado.");
        _personal.Playlists.Add(new() { Name = name }); PersistPersonalization();
    }
    public void RenamePlaylist(LocalPlaylist playlist, string name)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 100 || _personal.Playlists.Any(p => p != playlist && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("Nome inválido ou já utilizado.");
        playlist.Name = name; PersistPersonalization();
    }
    public void AddToPlaylist(LocalPlaylist playlist, Track track)
    { if (!playlist.Tracks.Any(t => t.Source == track.Source)) playlist.Tracks.Add(track); PersistPersonalization(); }
    public void RemoveFromPlaylist(LocalPlaylist playlist, Track track) { playlist.Tracks.Remove(track); PersistPersonalization(); }
    public void DeletePlaylist(LocalPlaylist playlist) { _personal.Playlists.Remove(playlist); PersistPersonalization(); }
    public void PinPlaylist(LocalPlaylist playlist) { playlist.Pinned = !playlist.Pinned; PersistPersonalization(); }
}
