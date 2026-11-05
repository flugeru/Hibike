using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hibike.Core.Models;

namespace Hibike.Core.Services;

public sealed record MusicArtist(string Id, string Name, string? Image);
public sealed record MusicChart(string Region, string Title, DateTime RetrievedUtc, IReadOnlyList<Track> Tracks, string? PlaylistId = null);
public sealed record ArtistCollection(string Id, string Title, string Kind, string? Image,
    MusicArtist? Artist = null);
public sealed record ArtistSection(string Title, string Id, string? Parameters);
public sealed record ArtistProfile(string Id, string Name, string? Image, IReadOnlyList<Track> Tracks, IReadOnlyList<ArtistCollection> Collections, IReadOnlyList<ArtistSection> Sections);

public static class MusicDiscovery
{
    public static async Task<ArtistProfile> ArtistAsync(string id, CancellationToken token)
    {
        if (!Regex.IsMatch(id, "^UC[\\w-]{22}$")) throw new ArgumentException("ID de artista inválido.");
        return ParseArtist(await RequestAsync("browse", new { browseId = id }, token), id);
    }
    public static ArtistProfile ParseArtist(string json, string id)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (id != "search" && Nodes(root, "messageRenderer").Any() && !Nodes(root, "musicTwoRowItemRenderer").Any() && !Nodes(root, "musicResponsiveListItemRenderer").Any())
            throw new InvalidOperationException("YouTube Music não disponibilizou este catálogo.");
        var meta = Nodes(root, "microformatDataRenderer").FirstOrDefault();
        var name = meta.ValueKind == JsonValueKind.Object && meta.TryGetProperty("title", out var title) ? title.GetString() ?? "Artista" : "Artista";
        var image = Nodes(meta, "url").Where(n => n.ValueKind == JsonValueKind.String).Select(n => n.GetString()).LastOrDefault(u => u?.StartsWith("https://") == true);
        if (image is null)
            foreach (var header in Nodes(root, "musicImmersiveHeaderRenderer").Concat(Nodes(root, "musicVisualHeaderRenderer")).Concat(Nodes(root, "musicHeaderRenderer")))
                image ??= Nodes(header, "url").Where(n => n.ValueKind == JsonValueKind.String).Select(n => n.GetString()).FirstOrDefault(u => u?.StartsWith("https://") == true);
        var tracks = new List<Track>();
        var collections = new List<ArtistCollection>();
        foreach (var item in Nodes(root, "musicTwoRowItemRenderer").Concat(Nodes(root, "musicResponsiveListItemRenderer")))
        {
            var columns = Nodes(item, "musicResponsiveListItemFlexColumnRenderer")
                .Select(column => column.TryGetProperty("text", out var text) ? text : default).ToArray();
            var titleNode = item.TryGetProperty("title", out var titleRuns) ? titleRuns : columns.FirstOrDefault();
            var label = Text(titleNode);
            if (string.IsNullOrWhiteSpace(label)) continue;
            var thumb = Nodes(item, "url").Where(n => n.ValueKind == JsonValueKind.String).Select(n => n.GetString()).LastOrDefault(u => u?.StartsWith("https://") == true);
            var endpoints = PrimaryEndpoints(item, titleNode).ToArray();
            var browse = endpoints.SelectMany(endpoint => Nodes(endpoint, "browseId")).Select(node => node.GetString()).FirstOrDefault();
            if (browse is not null && (browse.StartsWith("VL", StringComparison.Ordinal) || browse.StartsWith("MPRE", StringComparison.Ordinal)))
            {
                var subtitles = item.TryGetProperty("subtitle", out var subtitle) ? new[] { subtitle } : columns.Skip(1);
                var secondary = subtitles.ToArray();
                var type = string.Join(" ", secondary.Select(Text));
                var creditedArtist = secondary.SelectMany(node => Nodes(node, "runs"))
                    .Where(runs => runs.ValueKind == JsonValueKind.Array).SelectMany(runs => runs.EnumerateArray())
                    .Select(run => (Name: run.TryGetProperty("text", out var value) ? value.GetString() : null, Id: Nodes(run, "browseId").Select(node => node.GetString()).FirstOrDefault(IsArtistId)))
                    .FirstOrDefault(credit => credit.Id is not null && !string.IsNullOrWhiteSpace(credit.Name));
                var kind = !browse.StartsWith("MPRE", StringComparison.Ordinal) ? "Playlist"
                    : type.Contains("Single", StringComparison.OrdinalIgnoreCase) || type.Contains("EP", StringComparison.OrdinalIgnoreCase) ? "Single / EP" : "Álbum";
                collections.Add(new(browse, label, kind, thumb,
                    creditedArtist.Id is { } artistId ? new MusicArtist(artistId, creditedArtist.Name!, null) : null));
            }
            else if (!IsArtistId(browse))
            {
                var video = endpoints.SelectMany(endpoint => Nodes(endpoint, "videoId")).Select(node => node.GetString()).FirstOrDefault();
                if (video is not null) tracks.Add(new(video, label, name, "https://www.youtube.com/watch?v=" + video, Thumbnail: thumb));
            }
        }
        var sections = new List<ArtistSection>();
        foreach (var header in Nodes(root, "musicCarouselShelfBasicHeaderRenderer"))
        {
            if (!header.TryGetProperty("moreContentButton", out var more)) continue;
            var endpoint = Nodes(more, "browseEndpoint").FirstOrDefault();
            if (endpoint.ValueKind != JsonValueKind.Object || !endpoint.TryGetProperty("browseId", out var browseId)) continue;
            sections.Add(new(header.TryGetProperty("title", out var heading) ? Text(heading) : "Catálogo", browseId.GetString()!,
                endpoint.TryGetProperty("params", out var parameters) ? parameters.GetString() : null));
        }
        return new(id, name, image, tracks.DistinctBy(t => t.Source).ToArray(), collections.DistinctBy(c => c.Id).ToArray(), sections);
    }
    public static async Task<ArtistProfile> ArtistSectionAsync(ArtistSection section, CancellationToken token)
    {
        var json = await RequestAsync("browse", new { browseId = section.Id, @params = section.Parameters }, token);
        var profile = ParseArtist(json, section.Id);
        var tracks = profile.Tracks.ToList();
        var collections = profile.Collections.ToList();
        var seen = new HashSet<string>();
        while (true)
        {
            using var doc = JsonDocument.Parse(json);
            var next = Nodes(doc.RootElement, "nextContinuationData").SelectMany(n => Nodes(n, "continuation"))
                .Concat(Nodes(doc.RootElement, "continuationCommand").SelectMany(n => Nodes(n, "token")))
                .Where(n => n.ValueKind == JsonValueKind.String).Select(n => n.GetString()).FirstOrDefault();
            if (next is null) break;
            if (!seen.Add(next)) throw new InvalidOperationException("Continuação repetida no catálogo.");
            json = await RequestAsync("browse", new { }, token, next);
            ArtistProfile page;
            try { page = ParseArtist(json, section.Id); }
            catch (InvalidOperationException)
            {
                // ponytail: anonymous continuation can fail; retain loaded items and expose retry, never claim a complete catalog.
                return profile with { Tracks = tracks.DistinctBy(t => t.Source).ToArray(), Collections = collections.DistinctBy(c => c.Id).ToArray(), Sections = [section with { Title = "Catálogo parcial · tentar novamente" }] };
            }
            tracks.AddRange(page.Tracks); collections.AddRange(page.Collections);
        }
        return profile with { Tracks = tracks.DistinctBy(t => t.Source).ToArray(), Collections = collections.DistinctBy(c => c.Id).ToArray(), Sections = [] };
    }
    public static async Task<string?> AlbumPlaylistAsync(string id, CancellationToken token)
    {
        if (!Regex.IsMatch(id, "^MPRE[\\w-]+$")) throw new ArgumentException("Álbum inválido.");
        using var doc = JsonDocument.Parse(await RequestAsync("browse", new { browseId = id }, token));
        return Nodes(doc.RootElement, "playlistId").Where(n => n.ValueKind == JsonValueKind.String).Select(n => n.GetString()).FirstOrDefault();
    }
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15), MaxResponseContentBufferSize = 8_000_000 };
    // ponytail: anonymous WEB_REMIX is unofficial; schema failures hide remote sections, never substitute search results for rankings.
    private static async Task<string> RequestAsync(string endpoint, object body, CancellationToken token, string? continuation = null)
    {
        var query = continuation is null ? "" : "&continuation=" + Uri.EscapeDataString(continuation) + "&ctoken=" + Uri.EscapeDataString(continuation);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://music.youtube.com/youtubei/v1/" + endpoint + "?prettyPrint=false" + query);
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
    private static bool IsArtistId(string? id) => id is not null && Regex.IsMatch(id, "^UC[\\w-]{22}$");

    private static IEnumerable<JsonElement> PrimaryEndpoints(JsonElement item, JsonElement title)
    {
        if (item.TryGetProperty("navigationEndpoint", out var navigation)) yield return navigation;
        foreach (var endpoint in Nodes(title, "navigationEndpoint")) yield return endpoint;
        // Some renderers put these directly on the item instead of wrapping them in a navigation endpoint.
        if (item.TryGetProperty("browseEndpoint", out var browse)) yield return browse;
        if (item.TryGetProperty("watchEndpoint", out var watch)) yield return watch;
        if (item.TryGetProperty("playlistItemData", out var playlist)) yield return playlist;
    }

    public static IReadOnlyList<MusicArtist> ParseArtists(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var result = new List<MusicArtist>();
        foreach (var item in Nodes(doc.RootElement, "musicResponsiveListItemRenderer").Concat(Nodes(doc.RootElement, "musicTwoRowItemRenderer")))
        {
            var column = Nodes(item, "musicResponsiveListItemFlexColumnRenderer").FirstOrDefault();
            var title = item.TryGetProperty("title", out var itemTitle) ? itemTitle : column;
            var id = PrimaryEndpoints(item, title).SelectMany(endpoint => Nodes(endpoint, "browseId"))
                .Select(node => node.GetString()).FirstOrDefault(IsArtistId);
            var name = Text(title);
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
    public static async Task<IReadOnlyList<ArtistCollection>> SearchCollectionsAsync(string query, bool playlists, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length > 200) return [];
        var filter = playlists ? "Eg-KAQwIABAAGAAgACgBMABqChAEEAMQCRAFEAo=" : "EgWKAQIYAWoMEA4QChADEAQQCRAF";
        var result = ParseArtist(await RequestAsync("search", new { query = query.Trim(), @params = filter }, token), "search");
        return result.Collections.Where(entry => (entry.Kind == "Playlist") == playlists).ToArray();
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
