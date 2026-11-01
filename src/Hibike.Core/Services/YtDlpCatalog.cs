using System.Text.Json;
using Hibike.Core.Models;
namespace Hibike.Core.Services;

public sealed class YtDlpCatalog(ICommandRunner runner, string? dataDirectory = null) : IMusicCatalog
{
    private string[] Common
    {
        get
        {
            var args = new List<string> { "--ignore-config", "--socket-timeout", "10", "--retries", "1", "--extractor-retries", "0", "--no-playlist" };
            if (dataDirectory is not null)
            {
                args.AddRange(["--cache-dir", Path.Combine(dataDirectory, "extractor-cache")]);
                var cookies = Path.Combine(dataDirectory, "youtube-cookies.txt");
                if (File.Exists(cookies)) args.AddRange(["--cookies", cookies]);
            }
            return args.ToArray();
        }
    }

    public async Task<IReadOnlyList<Track>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        query = query.Trim();
        if (query.Length == 0) return [];
        // Explicit terminator keeps user input out of yt-dlp's option parser.
        var link = Uri.TryCreate(query, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http";
        var target = link || query.StartsWith("ytsearch", StringComparison.Ordinal) || query.StartsWith("scsearch", StringComparison.Ordinal)
            ? query : "ytsearch30:" + query;
        var result = await runner.RunAsync([..Common, "--yes-playlist", "--playlist-end", "100", "--flat-playlist", "--skip-download", "--quiet", "--dump-single-json", "--", target], cancellationToken);
        PlaybackDiagnostics.Write("Busca", "Metadados recebidos; nenhum arquivo de áudio foi baixado.");
        EnsureSuccess(result);
        return ParseSearch(result.Output);
    }

    public async Task<AudioSource> ResolveAsync(Track track, CancellationToken cancellationToken)
    {
        if (track.IsLocal) return new(track.Source, new Dictionary<string, string>(), track.Title, track.Artist, track.Thumbnail);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var options = new[] { "-f", "bestaudio", "--skip-download", "--dump-single-json" };
        ProcessResult result;
        var hasCookies = dataDirectory is not null && File.Exists(Path.Combine(dataDirectory, "youtube-cookies.txt"));
        var youtube = Uri.TryCreate(track.Source, UriKind.Absolute, out var provider) && (provider.Host == "youtube.com" || provider.Host.EndsWith(".youtube.com") || provider.Host == "youtu.be");
        if (!hasCookies && youtube)
        {
            // Official yt-dlp JS-less default client: avoids interpreting the web player on a cold start.
            result = await runner.RunAsync([..Common, ..options, "--extractor-args", "youtube:player_client=visionos", "--", track.Source], cancellationToken);
            if (result.ExitCode != 0)
            {
                PlaybackDiagnostics.Write("Extrator", "Cliente rápido indisponível; tentando clientes padrão.");
                result = await runner.RunAsync([..Common, ..options, "--", track.Source], cancellationToken);
            }
        }
        else result = await runner.RunAsync([..Common, ..options, "--", track.Source], cancellationToken);
        PlaybackDiagnostics.Write("Extrator", $"Resolução terminou em {watch.ElapsedMilliseconds} ms.");
        EnsureSuccess(result);
        using var document = JsonDocument.Parse(result.Output);
        var item = document.RootElement;
        var url = Text(item, "url") ?? throw new InvalidOperationException("O YT-DLP não encontrou áudio reproduzível.");
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (item.TryGetProperty("http_headers", out var values))
            foreach (var header in values.EnumerateObject())
                if (header.Value.ValueKind == JsonValueKind.String) headers[header.Name] = header.Value.GetString()!;
        PlaybackDiagnostics.Write("Extrator", "Áudio resolvido; formato=" + (Text(item, "format_id") ?? "?") + "; protocolo=" + (Text(item, "protocol") ?? "?"));
        return new(url, headers, track.Title, track.Artist, Text(item, "thumbnail") ?? track.Thumbnail,
            Number(item, "abr"), Text(item, "acodec"), Number(item, "asr"), Number(item, "audio_channels"), Text(item, "format_id"), Text(item, "ext"));
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
            if (!Uri.TryCreate(source, UriKind.Absolute, out _))
            {
                var extractor = Text(item, "ie_key") ?? Text(root, "extractor_key") ?? "Youtube";
                if (!extractor.Contains("Youtube", StringComparison.OrdinalIgnoreCase)) continue;
                source = "https://www.youtube.com/watch?v=" + id;
            }
            var duration = item.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number && d.TryGetDouble(out var seconds) ? seconds : 0;
            var thumbnail = Text(item, "thumbnail");
            if (thumbnail is null && item.TryGetProperty("thumbnails", out var thumbs) && thumbs.ValueKind == JsonValueKind.Array)
                thumbnail = thumbs.EnumerateArray().Select(t => Text(t, "url")).LastOrDefault(t => t is not null);
            tracks.Add(new(id, title, Text(item, "artist") ?? Text(item, "uploader") ?? Text(item, "channel") ?? "YouTube", source, duration, thumbnail));
        }
        return tracks.DistinctBy(t => t.Id).ToArray();
    }

    private static double? Number(JsonElement element, string name) => element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number) && number > 0 ? number : null;

    private static string? Text(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static void EnsureSuccess(ProcessResult result)
    {
        if (result.ExitCode != 0)
            {
            var message = result.Error.Trim();
            if (message.Contains("sign in", StringComparison.OrdinalIgnoreCase) || message.Contains("login required", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("O YouTube pediu verificação de sessão. Você pode importar cookies nas configurações. Veja o diagnóstico para o erro completo.");
            var lastError = message.Split('\n').LastOrDefault(line => line.Contains("ERROR:", StringComparison.OrdinalIgnoreCase)) ?? message;
            throw new InvalidOperationException("YT-DLP: " + (lastError.Length == 0 ? "Não foi possível acessar o conteúdo." : lastError[..Math.Min(lastError.Length, 240)]));
        }
    }
}
