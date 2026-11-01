using System.Text.Json;
using Hibiki.Core.Models;
namespace Hibiki.Core.Services;

public sealed class YtDlpCatalog(ICommandRunner runner) : IMusicCatalog
{
    private static readonly string[] Common = ["--ignore-config", "--no-warnings", "--no-cache-dir", "--socket-timeout", "20", "--no-playlist"];

    public async Task<IReadOnlyList<Track>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        query = query.Trim();
        if (query.Length == 0) return [];
        // Explicit terminator keeps user input out of yt-dlp's option parser.
        var target = Uri.TryCreate(query, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http"
            ? query : "ytsearch15:" + query;
        var result = await runner.RunAsync([..Common, "--flat-playlist", "--dump-single-json", "--", target], cancellationToken);
        EnsureSuccess(result);
        return ParseSearch(result.Output);
    }

    public async Task<AudioSource> ResolveAsync(Track track, CancellationToken cancellationToken)
    {
        if (track.IsLocal) return new(track.Source, new Dictionary<string, string>());
        var result = await runner.RunAsync([..Common, "-f", "bestaudio[ext=m4a]/bestaudio", "--dump-single-json", "--", track.Source], cancellationToken);
        EnsureSuccess(result);
        using var document = JsonDocument.Parse(result.Output);
        var item = document.RootElement;
        var url = Text(item, "url") ?? throw new InvalidOperationException("O YT-DLP não encontrou áudio reproduzível.");
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (item.TryGetProperty("http_headers", out var values))
            foreach (var header in values.EnumerateObject())
                if (header.Value.ValueKind == JsonValueKind.String) headers[header.Name] = header.Value.GetString()!;
        return new(url, headers);
    }

    public static IReadOnlyList<Track> ParseSearch(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var items = root.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Array
            ? entries.EnumerateArray().ToArray() : [root];
        var tracks = new List<Track>();
        foreach (var item in items)
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var id = Text(item, "id");
            var title = Text(item, "title");
            if (id is null || title is null) continue;
            var source = Text(item, "webpage_url") ?? Text(item, "url") ?? "https://www.youtube.com/watch?v=" + id;
            if (!Uri.TryCreate(source, UriKind.Absolute, out _)) source = "https://www.youtube.com/watch?v=" + id;
            var duration = item.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number && d.TryGetDouble(out var seconds) ? seconds : 0;
            var thumbnail = Text(item, "thumbnail");
            if (thumbnail is null && item.TryGetProperty("thumbnails", out var thumbs) && thumbs.ValueKind == JsonValueKind.Array)
                thumbnail = thumbs.EnumerateArray().Select(t => Text(t, "url")).LastOrDefault(t => t is not null);
            tracks.Add(new(id, title, Text(item, "artist") ?? Text(item, "uploader") ?? Text(item, "channel") ?? "YouTube", source, duration, thumbnail));
        }
        return tracks.DistinctBy(t => t.Id).ToArray();
    }

    private static string? Text(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static void EnsureSuccess(ProcessResult result)
    {
        if (result.ExitCode != 0)
            throw new InvalidOperationException("YT-DLP: " + (string.IsNullOrWhiteSpace(result.Error)
                ? "Não foi possível acessar o conteúdo." : result.Error.Trim()[..Math.Min(result.Error.Trim().Length, 500)]));
    }
}
