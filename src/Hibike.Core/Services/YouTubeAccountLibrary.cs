using System.Net.Http.Headers;
using System.Text.Json;
using Hibike.Core.Models;

namespace Hibike.Core.Services;

public sealed record AccountPlaylist(string Id, string Title, string? Thumbnail = null);

public sealed class YouTubeAccountLibrary(HttpClient client, string accessToken) : IDisposable
{
    private string? _credential = accessToken;
    private bool _webSession;
    public bool IsWebSession => _webSession;
    public string ExportWebSession() => _webSession && _credential is { } cookies ? cookies : throw new InvalidOperationException("Sessão web indisponível.");
    public string? DisplayName { get; private set; }
    public string? AvatarUrl { get; private set; }
    public void Dispose() => _credential = null;
    public static YouTubeAccountLibrary FromWebSession(HttpClient client, string cookies)
    {
        if (cookies.Length > 65536 || cookies.Any(c => char.IsControl(c)) || !cookies.Split(';').Any(c => c.Trim().StartsWith("SAPISID=", StringComparison.Ordinal) && c.Trim().Length > 8))
            throw new InvalidOperationException("Sessão YouTube Music inválida. Entre novamente.");
        return new(client, cookies) { _webSession = true };
    }
    public async Task ValidateSessionAsync(CancellationToken token)
    {
        using var json = await WebRequestAsync("account/account_menu", null, null, token);
        if (!Nodes(json.RootElement, "activeAccountHeaderRenderer").Any())
            throw new AccountSessionExpiredException();
        var header = Nodes(json.RootElement, "activeAccountHeaderRenderer").First();
        if (header.TryGetProperty("accountName", out var name)) DisplayName = WebText(name);
        AvatarUrl = WebImage(header);
    }
    public async Task<IReadOnlyList<AccountPlaylist>> PlaylistsAsync(CancellationToken token)
    {
        if (_webSession)
        {
            var playlists = new List<AccountPlaylist>();
            await foreach (var root in WebPagesAsync("FEmusic_liked_playlists", token))
                foreach (var item in Nodes(root, "musicTwoRowItemRenderer").Concat(Nodes(root, "musicResponsiveListItemRenderer")))
                {
                    var id = Nodes(item, "browseId").Select(n => n.GetString()).FirstOrDefault(s => s?.StartsWith("VL", StringComparison.Ordinal) == true);
                    if (id is null || id == "VLLM") continue;
                    var title = item.TryGetProperty("title", out var name) ? WebText(name) : Columns(item).FirstOrDefault() ?? "Playlist";
                    playlists.Add(new(id[2..], title, WebImage(item)));
                }
            return playlists.DistinctBy(p => p.Id).ToArray();
        }
        var results = new List<AccountPlaylist>();
        await foreach (var item in ItemsAsync("playlists?part=snippet&mine=true&maxResults=50", token))
        {
            var snippet = item.GetProperty("snippet");
            var image = snippet.TryGetProperty("thumbnails", out var thumbs) && thumbs.TryGetProperty("medium", out var thumb)
                ? thumb.GetProperty("url").GetString() : null;
            results.Add(new(item.GetProperty("id").GetString()!, snippet.GetProperty("title").GetString()!, image));
        }
        return results;
    }

    public async Task<IReadOnlyList<Track>> TracksAsync(string? playlistId, CancellationToken token)
    {
        var tracks = new List<Track>();
        string? continuation = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var page = 0; page < (_webSession ? 100 : 20); page++)
        {
            var result = await TrackPageAsync(playlistId, continuation, token);
            tracks.AddRange(result.Tracks);
            continuation = result.Continuation;
            if (continuation is null) return tracks.DistinctBy(track => track.Id).ToArray();
            if (!seen.Add(continuation)) throw new InvalidOperationException("Continuação repetida na biblioteca.");
        }
        if (_webSession) throw new InvalidOperationException("Biblioteca excedeu 100 páginas. Lista não foi carregada por completo.");
        return tracks.DistinctBy(track => track.Id).ToArray();
    }

    public async Task<(IReadOnlyList<Track> Tracks, string? Continuation)> TrackPageAsync(
        string? playlistId, string? continuation, CancellationToken token)
    {
        var tracks = new List<Track>();
        if (_webSession)
        {
            if (playlistId is not null && !System.Text.RegularExpressions.Regex.IsMatch(playlistId, "^[A-Za-z0-9_-]{1,200}$"))
                throw new ArgumentException("Playlist inválida.");
            using var json = await WebRequestAsync("browse", playlistId is null ? "VLLM" : "VL" + playlistId, continuation, token);
            var root = json.RootElement;
            if (Nodes(root, "error").Any()) throw new InvalidOperationException("Biblioteca indisponível. Reconecte sua conta.");
            var shelf = Nodes(root, "musicPlaylistShelfRenderer").Concat(Nodes(root, "musicPlaylistShelfContinuation"))
                .FirstOrDefault();
            var contents = shelf.ValueKind != JsonValueKind.Undefined && shelf.TryGetProperty("contents", out var shelfItems)
                ? shelfItems : continuation is null ? default
                : Nodes(root, "appendContinuationItemsAction").SelectMany(action => Nodes(action, "continuationItems")).FirstOrDefault();
            var items = contents.ValueKind == JsonValueKind.Array ? contents.EnumerateArray().ToArray() : [];
            foreach (var row in items)
            {
                if (!row.TryGetProperty("musicResponsiveListItemRenderer", out var item)) continue;
                var id = Nodes(item, "videoId").Select(node => node.GetString()).FirstOrDefault();
                var columns = Columns(item).ToArray();
                if (id is null || !System.Text.RegularExpressions.Regex.IsMatch(id, "^[A-Za-z0-9_-]{11}$") || columns.Length == 0) continue;
                var album = Nodes(item, "runs").Where(runs => runs.ValueKind == JsonValueKind.Array)
                    .SelectMany(runs => runs.EnumerateArray())
                    .Where(run => Nodes(run, "browseId").Any(value => value.ValueKind == JsonValueKind.String
                        && value.GetString()!.StartsWith("MPRE", StringComparison.Ordinal)))
                    .Select(run => run.TryGetProperty("text", out var text) ? text.GetString() : null)
                    .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));
                tracks.Add(new(id, columns[0], columns.ElementAtOrDefault(1) ?? "YouTube Music",
                    "https://www.youtube.com/watch?v=" + id, Thumbnail: WebImage(item), Album: album));
            }
            var last = items.LastOrDefault();
            var nextPage = last.ValueKind == JsonValueKind.Object && last.TryGetProperty("continuationItemRenderer", out var itemContinuation)
                ? WebContinuation(itemContinuation) : null;
            if (nextPage is null && shelf.ValueKind == JsonValueKind.Object && shelf.TryGetProperty("continuations", out var legacy))
                nextPage = WebContinuation(legacy);
            return (tracks, nextPage);
        }

        var endpoint = playlistId is null ? "videos?part=snippet&myRating=like&maxResults=50"
            : "playlistItems?part=snippet&maxResults=50&playlistId=" + Uri.EscapeDataString(playlistId);
        using var official = await ApiPageAsync(endpoint, continuation, token);
        foreach (var item in official.RootElement.GetProperty("items").EnumerateArray())
        {
            var snippet = item.GetProperty("snippet");
            var id = playlistId is null ? item.GetProperty("id").GetString()
                : snippet.GetProperty("resourceId").GetProperty("videoId").GetString();
            var title = snippet.GetProperty("title").GetString();
            if (string.IsNullOrWhiteSpace(id) || title is null or "Private video" or "Deleted video") continue;
            var artist = snippet.TryGetProperty("videoOwnerChannelTitle", out var owner) ? owner.GetString()
                : snippet.TryGetProperty("channelTitle", out var channel) ? channel.GetString() : null;
            string? image = null;
            if (snippet.TryGetProperty("thumbnails", out var thumbnails))
                foreach (var size in new[] { "high", "medium", "default" })
                    if (thumbnails.TryGetProperty(size, out var thumb)) { image = thumb.GetProperty("url").GetString(); break; }
            tracks.Add(new(id, title, artist ?? "YouTube", "https://www.youtube.com/watch?v=" + Uri.EscapeDataString(id), 0, image));
        }
        return (tracks, official.RootElement.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null);
    }

    private async Task<JsonDocument> ApiPageAsync(string endpoint, string? page, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/youtube/v3/" + endpoint
            + (page is null ? "" : "&pageToken=" + Uri.EscapeDataString(page)));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _credential ?? throw new InvalidOperationException("Conta desconectada."));
        using var response = await client.SendAsync(request, token);
        if (!response.IsSuccessStatusCode)
        {
            PlaybackDiagnostics.Write(DiagnosticSector.Google, "API YouTube", $"HTTP {(int)response.StatusCode}.");
            throw new InvalidOperationException($"Biblioteca YouTube: HTTP {(int)response.StatusCode}. Reconecte se sessão expirou; confira API, permissão e cota no Google Cloud.");
        }
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
    }

    private async IAsyncEnumerable<JsonElement> ItemsAsync(string endpoint,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
    {
        string? page = null;
        // ponytail: cap at 1000 entries per view; add explicit load-more when needed.
        for (var count = 0; count < 20; count++)
        {
            using var json = await ApiPageAsync(endpoint, page, token);
            foreach (var item in json.RootElement.GetProperty("items").EnumerateArray()) yield return item.Clone();
            page = json.RootElement.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null;
            if (page is null) yield break;
        }
    }
    private async IAsyncEnumerable<JsonElement> WebPagesAsync(string browseId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
    {
        string? continuation = null;
        var seen = new HashSet<string>();
        // ponytail: at most 100 pages per request; add explicit load-more for larger libraries.
        for (var page = 0; page < 100; page++)
        {
            using var json = await WebRequestAsync("browse", browseId, continuation, token);
            if (Nodes(json.RootElement, "error").Any()) throw new InvalidOperationException("Biblioteca indisponível. Reconecte sua conta.");
            yield return json.RootElement.Clone();
            continuation = WebContinuation(json.RootElement);
            if (continuation is null) yield break;
            if (!seen.Add(continuation)) throw new InvalidOperationException("Continuação repetida na biblioteca.");
        }
        throw new InvalidOperationException("Biblioteca excedeu 100 páginas. Lista não foi carregada por completo.");
    }
    private static string? WebContinuation(JsonElement root) =>
        Nodes(root, "nextContinuationData").SelectMany(node => Nodes(node, "continuation"))
            .Concat(Nodes(root, "continuationCommand").SelectMany(node => Nodes(node, "token")))
            .Where(node => node.ValueKind == JsonValueKind.String).Select(node => node.GetString()).FirstOrDefault();

    private async Task<JsonDocument> WebRequestAsync(string endpoint, string? browseId, string? continuation, CancellationToken token)
    {
        var cookies = _credential ?? throw new InvalidOperationException("Conta desconectada.");
        var sapisid = cookies.Split(';').Select(c => c.Trim()).FirstOrDefault(c => c.StartsWith("SAPISID=", StringComparison.Ordinal))?[8..]
            ?? throw new InvalidOperationException("Sessão expirada. Reconecte sua conta.");
        var time = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes($"{time} {sapisid} https://music.youtube.com")));
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://music.youtube.com/youtubei/v1/" + endpoint + "?prettyPrint=false");
        request.Headers.Add("Cookie", cookies);
        request.Headers.Add("Authorization", $"SAPISIDHASH {time}_{hash}");
        request.Headers.Add("Origin", "https://music.youtube.com");
        request.Headers.Referrer = new Uri("https://music.youtube.com/");
        request.Headers.Add("X-Goog-AuthUser", "0");
        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            context = new { client = new { clientName = "WEB_REMIX", clientVersion = "1.20261026.01.00", hl = "pt", gl = "BR" } },
            browseId = continuation is null ? browseId : null, continuation
        }), System.Text.Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request, token);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized) throw new AccountSessionExpiredException();
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            if (endpoint != "account/account_menu") await ValidateSessionAsync(token);
            else throw new AccountSessionExpiredException();
        }
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Biblioteca YouTube Music: HTTP {(int)response.StatusCode}. Tente conexão novamente.", null, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
    }
    private static IEnumerable<string> Columns(JsonElement item) => Nodes(item, "musicResponsiveListItemFlexColumnRenderer")
        .Select(n => n.TryGetProperty("text", out var text) ? WebText(text) : "");
    private static string WebText(JsonElement item) => item.TryGetProperty("runs", out var runs)
        ? string.Concat(runs.EnumerateArray().Select(n => n.TryGetProperty("text", out var text) ? text.GetString() : ""))
        : item.TryGetProperty("simpleText", out var simple) ? simple.GetString() ?? "" : "";
    private static string? WebImage(JsonElement item) => Nodes(item, "thumbnails").Where(n => n.ValueKind == JsonValueKind.Array)
        .SelectMany(n => n.EnumerateArray())
        .OrderByDescending(n => n.TryGetProperty("width", out var width) && width.TryGetInt32(out var w) ? w : 0)
        .Select(n => n.TryGetProperty("url", out var url) ? url.GetString() : null)
        .FirstOrDefault(u => Uri.TryCreate(u, UriKind.Absolute, out var uri) && uri.Scheme == "https");
    private static IEnumerable<JsonElement> Nodes(JsonElement item, string name)
    {
        if (item.ValueKind == JsonValueKind.Object)
            foreach (var property in item.EnumerateObject())
            {
                if (property.Name == name) yield return property.Value;
                foreach (var child in Nodes(property.Value, name)) yield return child;
            }
        else if (item.ValueKind == JsonValueKind.Array)
            foreach (var value in item.EnumerateArray()) foreach (var child in Nodes(value, name)) yield return child;
    }
}
