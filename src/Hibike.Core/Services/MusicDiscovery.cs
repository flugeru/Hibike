using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hibike.Core.Models;

namespace Hibike.Core.Services;

public sealed record MusicArtist(string Id, string Name, string? Image);
public sealed record MusicChart(string Region, string Title, DateTime RetrievedUtc, IReadOnlyList<Track> Tracks, string? PlaylistId = null);

public static class MusicDiscovery
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15), MaxResponseContentBufferSize = 8_000_000 };
    // ponytail: anonymous WEB_REMIX is unofficial; schema failures hide remote sections, never substitute search results for rankings.
    private static async Task<string> RequestAsync(string endpoint, object body, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://music.youtube.com/youtubei/v1/" + endpoint + "?prettyPrint=false");
        request.Headers.Add("Origin", "https://music.youtube.com");
        var values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(body))!;
        values["context"] = JsonSerializer.SerializeToElement(new { client = new { clientName = "WEB_REMIX", clientVersion = "1.20261026.01.00", hl = "en" } });
        request.Content = JsonContent.Create(values);
        using var response = await Client.SendAsync(request, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
    }
    public static IEnumerable<JsonElement> Nodes(JsonElement node, string key)
    {
        if (node.ValueKind == JsonValueKind.Object)
            foreach (var p in node.EnumerateObject())
            {
                if (p.Name == key) yield return p.Value;
                foreach (var child in Nodes(p.Value, key)) yield return child;
            }
        else if (node.ValueKind == JsonValueKind.Array)
            foreach (var item in node.EnumerateArray()) foreach (var child in Nodes(item, key)) yield return child;
    }
    private static string Text(JsonElement node) => string.Concat(Nodes(node, "text").Where(n => n.ValueKind == JsonValueKind.String).Select(n => n.GetString()));
    public static IReadOnlyList<MusicArtist> ParseArtists(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var result = new List<MusicArtist>();
        foreach (var item in Nodes(doc.RootElement, "musicResponsiveListItemRenderer"))
        {
            var id = Nodes(item, "browseId").Select(n => n.GetString()).FirstOrDefault(s => s is not null && Regex.IsMatch(s, "^UC[\\w-]{22}$"));
            var name = Nodes(item, "musicResponsiveListItemFlexColumnRenderer").Select(Text).FirstOrDefault();
            var image = Nodes(item, "url").Select(n => n.GetString()).LastOrDefault(s => s?.StartsWith("https://", StringComparison.Ordinal) == true);
            if (id is not null && !string.IsNullOrWhiteSpace(name)) result.Add(new(id, name, image));
        }
        return result.DistinctBy(a => a.Id).Take(30).ToArray();
    }
    public static async Task<IReadOnlyList<MusicArtist>> SearchArtistsAsync(string query, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length > 200) return [];
        return ParseArtists(await RequestAsync("search", new { query = query.Trim(), @params = "EgWKAQIgAQ==" }, token));
    }
    public static async Task<MusicChart?> ChartAsync(string region, CancellationToken token, bool trending = false)
    {
        if (!Regex.IsMatch(region, "^[A-Z]{2}$")) throw new ArgumentException("Região inválida.");
        using var doc = JsonDocument.Parse(await RequestAsync("browse", new { browseId = "FEmusic_charts", formData = new { selectedValues = new[] { region } } }, token));
        foreach (var item in Nodes(doc.RootElement, "musicTwoRowItemRenderer"))
        {
            if (!item.TryGetProperty("title", out var titleNode)) continue;
            var title = Text(titleNode);
            if (trending ? !title.StartsWith("Trending ", StringComparison.OrdinalIgnoreCase)
                : !title.Contains("Top Music Videos", StringComparison.OrdinalIgnoreCase)
                    || !(title.Contains("Daily", StringComparison.OrdinalIgnoreCase) || title.Contains("Weekly", StringComparison.OrdinalIgnoreCase))) continue;
            var playlist = Nodes(item, "browseId").Select(n => n.GetString()).FirstOrDefault(s => s is not null && (s.StartsWith("VLPL", StringComparison.Ordinal) || s.StartsWith("VLOLAK", StringComparison.Ordinal)));
            if (playlist is null) continue;
            var tracks = YouTubeMusicRadio.Parse(await RequestAsync("next", new { playlistId = playlist[2..], isAudioOnly = true }, token));
            if (tracks.Count > 0) return new(region, title, DateTime.UtcNow, tracks.Take(30).ToArray(), playlist[2..]);
        }
        return null;
    }
}

public sealed record RecommendationWeights(double Artists = .30, double History = .25, double Favorites = .20, double Similarity = .15, double Discovery = .10);
public static class MusicRecommendations
{
    public static IReadOnlyList<Track> Rank(IEnumerable<Track> candidates, IReadOnlySet<string> artists,
        IReadOnlyList<Track> history, IReadOnlyList<Track> favorites, IReadOnlySet<string> dismissed,
        Func<Track, double> affinity, RecommendationWeights weights, int limit = 24)
    {
        if (new[] { weights.Artists, weights.History, weights.Favorites, weights.Similarity, weights.Discovery }.Any(w => !double.IsFinite(w) || w < 0))
            throw new ArgumentException("Pesos inválidos.");
        string Artist(Track t) => TrackTextFormatter.Artist(t.MusicArtist ?? t.Artist);
        var known = history.Select(Artist).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var liked = favorites.Select(Artist).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        return candidates.Where(t => !dismissed.Contains(t.Source) && !string.IsNullOrWhiteSpace(t.Source))
            .DistinctBy(t => t.Source).OrderByDescending(t =>
                weights.Artists * (artists.Contains(Artist(t)) ? 1 : 0)
                + weights.History * Math.Clamp(affinity(t) / 5, -1, 1)
                + weights.Favorites * (liked.Contains(Artist(t)) ? 1 : 0)
                + weights.Similarity * (known.Contains(Artist(t)) ? 1 : 0)
                + weights.Discovery * (!known.Contains(Artist(t)) ? 1 : 0)
                - (history.Take(5).Any(h => h.Source == t.Source) ? .5 : 0))
            .Where(t => { var name = Artist(t); var count = counts.GetValueOrDefault(name); if (count >= 3) return false; counts[name] = count + 1; return true; })
            .Take(limit).ToArray();
    }
}
