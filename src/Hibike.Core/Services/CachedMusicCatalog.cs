using System.Collections.Concurrent;
using Hibike.Core.Models;
namespace Hibike.Core.Services;

// Caches signed stream addresses in memory, never media bytes. Entries expire quickly.
public sealed class CachedMusicCatalog(IMusicCatalog inner) : IMusicCatalog
{
    private readonly ConcurrentDictionary<string, (AudioSource Source, DateTimeOffset Expires)> _cache = new();
    private readonly ConcurrentDictionary<string, Lazy<Task<AudioSource>>> _inflight = new();
    public Task<IReadOnlyList<Track>> SearchAsync(string query, CancellationToken token) => inner.SearchAsync(query, token);
    public Task<IReadOnlyList<Track>> BrowsePageAsync(string source, int page, CancellationToken token) => inner.BrowsePageAsync(source, page, token);
    public Task<IReadOnlyList<Track>> RadioAsync(Track seed, CancellationToken token) => inner.RadioAsync(seed, token);
    public async Task<AudioSource> ResolveAsync(Track track, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (track.IsLocal) return await inner.ResolveAsync(track, token);
        if (_cache.TryGetValue(track.Source, out var entry) && entry.Expires > DateTimeOffset.UtcNow)
        { PlaybackDiagnostics.Write("Cache", "Link de áudio reutilizado; sem nova extração."); return entry.Source; }
        var work = _inflight.GetOrAdd(track.Source, key => new Lazy<Task<AudioSource>>(async () =>
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
                var source = await inner.ResolveAsync(track, timeout.Token);
                if (_cache.Count > 50) _cache.Clear();
                _cache[track.Source] = (source, DateTimeOffset.UtcNow.AddMinutes(4));
                return source;
            }
            finally { _inflight.TryRemove(track.Source, out _); }
        }));
        return await work.Value.WaitAsync(token);
    }
    public void Clear() => _cache.Clear();
    public void Invalidate(Track track) => _cache.TryRemove(track.Source, out _);
    public async Task WarmAsync(IEnumerable<Track> tracks)
    {
        foreach (var track in tracks)
        {
            try { await ResolveAsync(track, CancellationToken.None); }
            catch (Exception) { /* Prefetch failures must not interrupt the selected track. */ }
        }
    }
}
