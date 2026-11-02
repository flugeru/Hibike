using System.Net.Http.Headers;
using System.Text.Json;
using Hibike.Core.Models;

namespace Hibike.Core.Services;

public sealed record AccountPlaylist(string Id, string Title, string? Thumbnail = null);

public sealed class YouTubeAccountLibrary(HttpClient client, string accessToken)
{
    public async Task<IReadOnlyList<AccountPlaylist>> PlaylistsAsync(CancellationToken token)
    {
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
        var results = new List<Track>();
        var endpoint = playlistId is null ? "videos?part=snippet&myRating=like&maxResults=50"
            : "playlistItems?part=snippet&maxResults=50&playlistId=" + Uri.EscapeDataString(playlistId);
        await foreach (var item in ItemsAsync(endpoint, token))
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
            results.Add(new(id, title, artist ?? "YouTube", "https://www.youtube.com/watch?v=" + Uri.EscapeDataString(id), 0, image));
        }
        return results.DistinctBy(t => t.Id).ToArray();
    }

    private async IAsyncEnumerable<JsonElement> ItemsAsync(string endpoint,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
    {
        string? page = null;
        // ponytail: cap at 1000 entries per view; add explicit load-more when needed.
        for (var count = 0; count < 20; count++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/youtube/v3/" + endpoint
                + (page is null ? "" : "&pageToken=" + Uri.EscapeDataString(page)));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await client.SendAsync(request, token);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Biblioteca YouTube: HTTP {(int)response.StatusCode}. Reconecte se sessão expirou; confira API, permissão e cota no Google Cloud.");
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            foreach (var item in json.RootElement.GetProperty("items").EnumerateArray()) yield return item.Clone();
            page = json.RootElement.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null;
            if (page is null) yield break;
        }
    }
}
