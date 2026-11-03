using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hibike.Core.Models;

namespace Hibike.Core.Services;

public sealed record RadioPage(IReadOnlyList<Track> Tracks, string? Continuation);

public static class YouTubeMusicRadio
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(12), MaxResponseContentBufferSize = 4_000_000 };
    public static async Task<IReadOnlyList<Track>> FetchAsync(Track seed, CancellationToken token) =>
        (await FetchPageAsync(seed, null, token)).Tracks;

    public static async Task<RadioPage> FetchPageAsync(Track seed, string? continuation, CancellationToken token)
    {
        if (seed.IsLocal || !Uri.TryCreate(seed.Source, UriKind.Absolute, out var uri)
            || !(uri.Host == "youtu.be" || uri.Host == "youtube.com" || uri.Host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase))) return new([], null);
        var id = uri.Host == "youtu.be" ? uri.AbsolutePath.Trim('/')
            : Regex.Match(uri.Query, @"(?:\?|&)v=([\w-]{11})(?:&|$)").Groups[1].Value;
        if (!Regex.IsMatch(id, @"^[\w-]{11}$")) return new([], null);
        // ponytail: unofficial anonymous WEB_REMIX endpoint; fall back to catalog search if its schema changes.
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://music.youtube.com/youtubei/v1/next?prettyPrint=false");
        request.Headers.Add("Origin", "https://music.youtube.com");
        var context = new { client = new { clientName = "WEB_REMIX", clientVersion = "1.20261026.01.00", hl = "en" } };
        request.Content = continuation is not null
            ? JsonContent.Create(new { context, continuation })
            : JsonContent.Create(new
        {
            context,
            videoId = id, playlistId = "RDAMVM" + id, isAudioOnly = true,
            enablePersistentPlaylistPanel = true, tunerSettingValue = "AUTOMIX_SETTING_NORMAL", @params = "wAEB"
        });
        using var response = await Client.SendAsync(request, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return ParsePage(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false));
    }
    public static IReadOnlyList<Track> Parse(string json) => ParsePage(json).Tracks;

    public static RadioPage ParsePage(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var tracks = new List<Track>();
        void Visit(JsonElement node)
        {
            if (node.ValueKind == JsonValueKind.Array) { foreach (var item in node.EnumerateArray()) Visit(item); return; }
            if (node.ValueKind != JsonValueKind.Object) return;
            if (node.TryGetProperty("playlistPanelVideoWrapperRenderer", out var wrapper))
            {
                var ids = new List<string>();
                void Collect(JsonElement value)
                {
                    if (value.ValueKind == JsonValueKind.Object)
                        foreach (var property in value.EnumerateObject())
                        { if (property.Name == "videoId" && property.Value.ValueKind == JsonValueKind.String) ids.Add(property.Value.GetString()!); else Collect(property.Value); }
                    else if (value.ValueKind == JsonValueKind.Array) foreach (var child in value.EnumerateArray()) Collect(child);
                }
                Collect(wrapper);
                var start = tracks.Count; Visit(wrapper);
                for (var i = start; i < tracks.Count; i++) tracks[i] = tracks[i] with { EquivalentVideoIds = ids.Distinct().ToArray() };
                return;
            }
            if (node.TryGetProperty("playlistPanelVideoRenderer", out var video))
            {
                var id = video.TryGetProperty("videoId", out var v) ? v.GetString() : null;
                var title = video.TryGetProperty("title", out var t) ? Text(t) : "";
                if (id is null || !Regex.IsMatch(id, @"^[\w-]{11}$") || string.IsNullOrWhiteSpace(title)) return;
                var artists = new List<string>(); string? album = null, artistId = null, albumId = null;
                if (video.TryGetProperty("longBylineText", out var byline) && byline.TryGetProperty("runs", out var runs))
                    foreach (var run in runs.EnumerateArray())
                    {
                        if (!run.TryGetProperty("navigationEndpoint", out var endpoint) || !endpoint.TryGetProperty("browseEndpoint", out var browse)
                            || !browse.TryGetProperty("browseId", out var bid)) continue;
                        var browseId = bid.GetString() ?? "";
                        if (browseId.StartsWith("UC", StringComparison.Ordinal)) { artists.Add(Text(run)); artistId ??= browseId; }
                        if (browseId.StartsWith("MPRE", StringComparison.Ordinal)) { album = Text(run); albumId = browseId; }
                    }
                var artist = string.Join(", ", artists);
                if (artist.Length == 0 && video.TryGetProperty("shortBylineText", out var shortByline)) artist = Text(shortByline);
                double duration = 0;
                if (video.TryGetProperty("lengthText", out var length))
                    foreach (var part in Text(length).Split(':')) { if (!int.TryParse(part, out var n)) { duration = 0; break; } duration = duration * 60 + n; }
                string? thumbnail = null;
                if (video.TryGetProperty("thumbnail", out var thumb) && thumb.TryGetProperty("thumbnails", out var thumbs) && thumbs.GetArrayLength() > 0)
                    thumbnail = thumbs.EnumerateArray().LastOrDefault().TryGetProperty("url", out var url) ? url.GetString() : null;
                tracks.Add(new(id, title, artist, "https://www.youtube.com/watch?v=" + id, duration, thumbnail,
                    Album: album, MusicTitle: title, MusicArtist: artists.Count > 0 ? artist : null, ArtistId: artistId, AlbumId: albumId));
                return;
            }
            foreach (var property in node.EnumerateObject()) Visit(property.Value);
        }
        Visit(doc.RootElement);
        var panels = MusicDiscovery.Nodes(doc.RootElement, "playlistPanelContinuation")
            .Concat(MusicDiscovery.Nodes(doc.RootElement, "playlistPanelRenderer"));
        var next = panels.Where(p => p.TryGetProperty("continuations", out _))
            .SelectMany(p => MusicDiscovery.Nodes(p.GetProperty("continuations"), "continuation"))
            .FirstOrDefault(n => n.ValueKind == JsonValueKind.String);
        return new(tracks.DistinctBy(t => t.Id).Take(100).ToArray(),
            next.ValueKind == JsonValueKind.String ? next.GetString() : null);
    }
    private static string Text(JsonElement node) => node.TryGetProperty("simpleText", out var simple) ? simple.GetString() ?? ""
        : node.TryGetProperty("text", out var text) ? text.GetString() ?? ""
        : node.TryGetProperty("runs", out var runs) ? string.Concat(runs.EnumerateArray().Select(Text)) : "";
}
