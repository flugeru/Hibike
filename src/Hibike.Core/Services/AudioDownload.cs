using Hibike.Core.Models;

namespace Hibike.Core.Services;

public static class AudioDownload
{
    private static readonly HttpClient Client = new() { Timeout = Timeout.InfiniteTimeSpan };
    public static async Task<string> SaveAsync(AudioSource source, string directory, IProgress<string>? progress, CancellationToken token, long maxBytes = 1_000_000_000)
    {
        if (!Uri.TryCreate(source.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https")
            throw new InvalidOperationException("Download exige stream HTTPS direto.");
        var extension = source.Container?.ToLowerInvariant();
        if (extension is not ("m4a" or "webm" or "mp3" or "ogg" or "opus" or "flac" or "wav" or "aac"))
            throw new InvalidOperationException("Contêiner não suportado para download sem conversão.");
        if (maxBytes <= 0) throw new IOException("Limite de armazenamento offline atingido.");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        foreach (var header in source.Headers) request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > maxBytes) throw new IOException("A faixa excede o espaço offline disponível.");
        var type = response.Content.Headers.ContentType?.MediaType ?? "";
        if (type.Contains("mpegurl", StringComparison.OrdinalIgnoreCase) || type.Contains("dash", StringComparison.OrdinalIgnoreCase) || type.Contains("text/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Playlist/manifesto exige download pelo extrator; stream direto indisponível.");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + "." + extension);
        var temp = path + ".part";
        try
        {
            await using (var input = await response.Content.ReadAsStreamAsync(token))
            await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            {
                var buffer = new byte[65536]; long total = 0; var last = DateTime.MinValue; int count;
                while ((count = await input.ReadAsync(buffer, token)) > 0)
                {
                    total += count;
                    if (total > maxBytes) throw new IOException("A faixa excede o espaço offline disponível.");
                    await output.WriteAsync(buffer.AsMemory(0, count), token);
                    if ((DateTime.UtcNow - last).TotalMilliseconds >= 500)
                    { progress?.Report($"Baixando: {total / 1048576d:0.0} MB"); last = DateTime.UtcNow; }
                }
                if (total == 0 || response.Content.Headers.ContentLength is { } length && total != length) throw new IOException("Download incompleto.");
            }
            token.ThrowIfCancellationRequested(); File.Move(temp, path); return path;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
