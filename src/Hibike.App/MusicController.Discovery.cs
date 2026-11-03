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
    public bool HomeLoading { get; private set; }
    public string HomeMessage { get; private set; } = "";
    private DateTime _homeUpdated;
    private bool _discoveryCacheLoaded;
    private sealed record DiscoveryCache(DateTime SavedUtc, string Region, Track[] Recommendations, Track[] Related, string Seed, MusicChart? Regional, MusicChart? Global, MusicChart? Trending = null);
    public void CompleteOnboarding(IEnumerable<MusicArtist> artists, IEnumerable<string> genres)
    {
        var selected = artists.DistinctBy(a => a.Id).ToList();
        if (selected.Count is > 0 and < 3 || selected.Count > 20 || selected.Any(a => !System.Text.RegularExpressions.Regex.IsMatch(a.Id, "^UC[\\w-]{22}$") || string.IsNullOrWhiteSpace(a.Name)))
            throw new ArgumentException("Escolha entre 3 e 20 artistas, ou pule esta etapa.");
        var styles = genres.Distinct().ToList();
        if (styles.Count > 30 || styles.Any(s => string.IsNullOrWhiteSpace(s) || s.Length > 60)) throw new ArgumentException("Gêneros inválidos.");
        var old = (_personal.Artists, _personal.Genres, _personal.OnboardingCompleted);
        _personal.Artists = selected; _personal.Genres = styles; _personal.OnboardingCompleted = true;
        try { SavePersonalization(); }
        catch { (_personal.Artists, _personal.Genres, _personal.OnboardingCompleted) = old; throw; }
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
    public async Task RefreshRecommendationsAsync(bool force = false)
    {
        if (HomeLoading) return;
        HomeLoading = true;
        try
        {
            var path = Path.Combine(DataDirectory, "discovery-cache.json");
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
            var token = timeout.Token;
            var candidates = new List<Track>();
            var region = ChartRegion;
            async Task LoadChart(bool global)
            {
                var existing = global ? GlobalChart : RegionalChart;
                if (existing is not null && DateTime.UtcNow - existing.RetrievedUtc < TimeSpan.FromHours(6)) return;
                try
                {
                    var chart = await MusicDiscovery.ChartAsync(global ? "ZZ" : region, token);
                    if (region != ChartRegion) return;
                    if (global) GlobalChart = chart ?? GlobalChart; else RegionalChart = chart ?? RegionalChart;
                    Changed?.Invoke();
                }
                catch (Exception e) when (e is HttpRequestException or OperationCanceledException or JsonException) { PlaybackDiagnostics.Write("Rankings", e.Message); }
            }
            async Task LoadTrending()
            {
                if (TrendingChart is not null && DateTime.UtcNow - TrendingChart.RetrievedUtc < TimeSpan.FromHours(3)) return;
                try { var chart = await MusicDiscovery.ChartAsync(region, token, trending: true); if (region == ChartRegion) TrendingChart = chart ?? TrendingChart; }
                catch (Exception e) when (e is HttpRequestException or OperationCanceledException or JsonException) { PlaybackDiagnostics.Write("Tendências", e.Message); }
            }
            var charts = Task.WhenAll(LoadChart(false), LoadChart(true), LoadTrending());
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
            await charts;
            candidates.AddRange(RegionalChart?.Tracks ?? []);
            var ranked = MusicRecommendations.Rank(candidates, FavoriteArtists.Select(a => a.Name).ToHashSet(StringComparer.OrdinalIgnoreCase),
                History.ToArray(), Favorites.ToArray(), _personal.Dismissed, Affinity, _personal.Weights);
            if (ranked.Count > 0) { HomeRecommendations = ranked; HomeMessage = ""; }
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
        finally { HomeLoading = false; Changed?.Invoke(); }
    }
}
