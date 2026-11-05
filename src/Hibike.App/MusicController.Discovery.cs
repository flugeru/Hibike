using System.Text.Json;
using Hibike.Core.Models;
using Hibike.Core.Services;

namespace Hibike.App;

public sealed partial class MusicController
{
    public bool OnboardingCompleted => _personal.OnboardingCompleted;
    public IReadOnlyList<MusicArtist> FavoriteArtists => _personal.Artists;
    public IReadOnlyList<string> FavoriteGenres => _personal.Genres;
    public string ChartRegion => _personal.Region;
    public IReadOnlyList<Track> HomeRecommendations { get; private set; } = [];
    public IReadOnlyList<Track> HomeRelated { get; private set; } = [];
    public string HomeSeed { get; private set; } = "";
    public MusicChart? RegionalChart { get; private set; }
    public MusicChart? GlobalChart { get; private set; }
    public MusicChart? TrendingChart { get; private set; }
    public bool HomeLoading => _recommendationsRefresh is not null;
    public string HomeMessage { get; private set; } = "";
    private DateTime _homeUpdated;
    private bool _discoveryCacheLoaded;
    private TaskCompletionSource? _recommendationsRefresh;
    private CancellationTokenSource? _recommendationsNetworkRequest;
    private sealed record DiscoveryCache(DateTime SavedUtc, string Region, Track[] Recommendations, Track[] Related, string Seed, MusicChart? Regional, MusicChart? Global, MusicChart? Trending = null);
    public async Task CompleteOnboardingAsync(IEnumerable<MusicArtist> artists, IEnumerable<string> genres)
    {
        var selected = artists.DistinctBy(a => a.Id).ToList();
        if (selected.Count is > 0 and < 3 || selected.Count > 20 || selected.Any(a => !System.Text.RegularExpressions.Regex.IsMatch(a.Id, "^UC[\\w-]{22}$") || string.IsNullOrWhiteSpace(a.Name)))
            throw new ArgumentException("Escolha entre 3 e 20 artistas, ou pule esta etapa.");
        var styles = genres.Distinct().ToList();
        if (styles.Count > 30 || styles.Any(s => string.IsNullOrWhiteSpace(s) || s.Length > 60)) throw new ArgumentException("Gêneros inválidos.");
        var old = (_personal.Artists, _personal.Genres, _personal.OnboardingCompleted);
        _personal.Artists = selected; _personal.Genres = styles; _personal.OnboardingCompleted = true;
        try { SavePersonalization(); await FlushPendingSavesAsync(); }
        catch { (_personal.Artists, _personal.Genres, _personal.OnboardingCompleted) = old; SavePersonalization(); throw; }
        _discoveryCacheLoaded = true; _homeUpdated = DateTime.MinValue; Changed?.Invoke();
    }
    public void SetChartRegion(string region)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(region, "^[A-Z]{2}$")) throw new ArgumentException("Região inválida.");
        _personal.Region = region; RegionalChart = TrendingChart = null; _homeUpdated = DateTime.MinValue; PersistPersonalization();
    }
    public void DismissRecommendation(Track track)
    {
        _personal.Dismissed.Add(track.Source); HomeRecommendations = HomeRecommendations.Where(t => t.Source != track.Source).ToArray();
        HomeRelated = HomeRelated.Where(t => t.Source != track.Source).ToArray(); PersistPersonalization();
    }
    public void ResetRecommendationFeedback() { _personal.Dismissed.Clear(); _homeUpdated = DateTime.MinValue; PersistPersonalization(); }
    private void RerankHome()
    {
        HomeRecommendations = MusicRecommendations.Rank(HomeRecommendations.Concat(HomeRelated), FavoriteArtists.Select(a => a.Name).ToHashSet(StringComparer.OrdinalIgnoreCase),
            History.ToArray(), Favorites.ToArray(), _personal.Dismissed, Affinity, _personal.Weights);
    }
    public Task<IReadOnlyList<MusicArtist>> SearchArtistsAsync(string query, CancellationToken token) => MusicDiscovery.SearchArtistsAsync(query, token);
    public Task<IReadOnlyList<Track>> SearchArtistTracksAsync(MusicArtist artist, CancellationToken token) => _catalog.SearchAsync("https://www.youtube.com/channel/" + artist.Id + "/videos", token);
    public Task<IReadOnlyList<Track>> SearchChannelAsync(string source, CancellationToken token) => _catalog.SearchAsync(source.TrimEnd('/') + "/videos", token);
    public Task<IReadOnlyList<Track>> BrowsePageAsync(string source, int page, CancellationToken token) => _catalog.BrowsePageAsync(source, page, token);
    public async Task RefreshRecommendationsAsync(bool force = false)
    {
        if (!CanUseOnline) return;
        if (_recommendationsRefresh is { } pending)
        {
            await pending.Task;
            return;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _recommendationsRefresh = completion;
        try
        {
            // Keep legacy chart-seeded cache untouched; this cache contains only personalized candidates.
            var path = Path.Combine(DataDirectory, "discovery-personalized-cache.json");
            if (!_discoveryCacheLoaded)
            {
                _discoveryCacheLoaded = true;
                try
                {
                    if (File.Exists(path) && JsonSerializer.Deserialize<DiscoveryCache>(await File.ReadAllTextAsync(path)) is { } cache
                        && cache.Recommendations is not null && cache.Related is not null && cache.Region == ChartRegion
                        && cache.Recommendations.Concat(cache.Related).All(t => t is not null && !string.IsNullOrWhiteSpace(t.Source)))
                    {
                        HomeRecommendations = cache.Recommendations.Where(t => !_personal.Dismissed.Contains(t.Source)).ToArray();
                        HomeRelated = cache.Related.Where(t => !_personal.Dismissed.Contains(t.Source)).ToArray(); HomeSeed = cache.Seed;
                        RegionalChart = cache.Regional; GlobalChart = cache.Global; TrendingChart = cache.Trending; _homeUpdated = cache.SavedUtc; Changed?.Invoke();
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { PlaybackDiagnostics.Write("Descoberta", e.Message); }
            }
            if (!force && DateTime.UtcNow - _homeUpdated < TimeSpan.FromMinutes(30)) return;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            _recommendationsNetworkRequest = timeout;
            var token = timeout.Token;
            var candidates = new List<Track>();
            var region = ChartRegion;
            var artistNames = FavoriteArtists.Select(a => a.Name).Concat(History.OrderByDescending(Affinity).Select(t => TrackTextFormatter.Artist(t.Artist)))
                .Concat(Favorites.Select(t => TrackTextFormatter.Artist(t.Artist))).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            // Rotate seed daily; cap network work rather than query all 20 artists at startup.
            var offset = artistNames.Length == 0 ? 0 : (int)(DateTime.UtcNow.Date - DateTime.UnixEpoch).TotalDays % artistNames.Length;
            var queries = artistNames.Skip(offset).Concat(artistNames.Take(offset)).Take(3)
                .Concat(FavoriteGenres.Take(2).Select(g => g + " music")).Distinct().ToArray();
            foreach (var query in queries)
            {
                try { candidates.AddRange(await _catalog.SearchAsync(query, token)); }
                catch (Exception e) when (e is not OutOfMemoryException) { HomeMessage = "Sem conexão com catálogo. Conteúdo salvo preservado."; PlaybackDiagnostics.Write("Descoberta", e.Message); }
                if (token.IsCancellationRequested) break;
            }
            var seed = History.FirstOrDefault(t => !t.IsLocal) ?? candidates.FirstOrDefault(t => !t.IsLocal);
            if (seed is not null && !token.IsCancellationRequested)
                try { HomeRelated = (await _catalog.RadioAsync(seed, token)).Where(t => t.Source != seed.Source && !_personal.Dismissed.Contains(t.Source)).Take(20).ToArray(); HomeSeed = TrackTextFormatter.Artist(seed.Artist); candidates.AddRange(HomeRelated); }
                catch (Exception e) when (e is not OutOfMemoryException) { PlaybackDiagnostics.Write("Descoberta", e.Message); }
            if (!CanUseOnline) return;
            var ranked = MusicRecommendations.Rank(candidates, FavoriteArtists.Select(a => a.Name).ToHashSet(StringComparer.OrdinalIgnoreCase),
                History.ToArray(), Favorites.ToArray(), _personal.Dismissed, Affinity, _personal.Weights);
            var enriched = new List<Track>();
            foreach (var track in ranked)
            {
                var result = track;
                if (track.IsArtist && track.Thumbnail is null && !token.IsCancellationRequested)
                {
                    try
                    {
                        var artists = await SearchArtistsAsync(track.Title, token);
                        var match = artists.FirstOrDefault(a => track.Source.TrimEnd('/').EndsWith("/" + a.Id, StringComparison.Ordinal));
                        if (match is not null) result = track with { Thumbnail = match.Image };
                    }
                    catch (Exception e) when (e is HttpRequestException or OperationCanceledException or JsonException) { PlaybackDiagnostics.Write("Imagem do artista", e.Message); }
                }
                enriched.Add(result);
            }
            if (ranked.Count > 0) { HomeRecommendations = enriched; HomeMessage = ""; }
            else if (HomeRecommendations.Count == 0) HomeMessage = "Escolha artistas ou busque músicas para descobrir seu som.";
            _homeUpdated = DateTime.UtcNow;
            try
            {
                Directory.CreateDirectory(DataDirectory);
                await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(new DiscoveryCache(_homeUpdated, region, HomeRecommendations.ToArray(), HomeRelated.ToArray(), HomeSeed, RegionalChart, GlobalChart, TrendingChart)));
                File.Move(path + ".tmp", path, true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { PlaybackDiagnostics.Write("Descoberta", e.Message); }
        }
        catch (Exception e) when (e is not OutOfMemoryException) { HomeMessage = "Descoberta indisponível. Sua biblioteca continua disponível."; PlaybackDiagnostics.Write("Descoberta", e.Message); }
        finally
        {
            _recommendationsNetworkRequest = null;
            _recommendationsRefresh = null;
            completion.SetResult();
            Changed?.Invoke();
        }
    }
}
