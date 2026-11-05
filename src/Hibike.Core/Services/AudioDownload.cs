using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hibike.Core.Models;

namespace Hibike.Core.Services;

/// <param name="Bytes">Bytes durably transferred so far, including a validated resumed portion.</param>
/// <param name="TotalBytes">Expected file length when supplied by the origin.</param>
/// <param name="BytesPerSecond">Measured throughput over the most recent reporting interval.</param>
public readonly record struct AudioDownloadProgress(long Bytes, long? TotalBytes, double BytesPerSecond = 0);

public static class AudioDownload
{
    private const int BufferSize = 128 * 1024;
    private const int MaxAttempts = 3;
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(350);
    private static readonly HttpClient Client = new() { Timeout = Timeout.InfiniteTimeSpan };

    private sealed record PartialMetadata(string? ETag, DateTimeOffset? LastModified, long? TotalBytes);

    /// <summary>
    /// Streams one audio file with bounded retries. A stable key allows a stopped transfer
    /// to resume after the source URL is refreshed, provided the origin supplies an HTTP validator.
    /// A cancelled transfer retains .part and .json; only a complete, length-checked file is published.
    /// </summary>
    public static async Task<string> SaveAsync(
        AudioSource source,
        string directory,
        IProgress<AudioDownloadProgress>? progress,
        CancellationToken token,
        string? stableKey = null,
        HttpClient? client = null)
    {
        if (!Uri.TryCreate(source.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Download exige stream HTTPS direto.");

        var extension = source.Container?.ToLowerInvariant();
        if (extension is not ("m4a" or "webm" or "mp3" or "ogg" or "opus" or "flac" or "wav" or "aac"))
            throw new InvalidOperationException("Contêiner não suportado para download sem conversão.");

        Directory.CreateDirectory(directory);
        // Stable file identity is distinct from short-lived, signed CDN URLs.
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stableKey ?? source.Url))).ToLowerInvariant();
        var destination = Path.Combine(directory, identity + "." + extension);
        var temporary = destination + ".part";
        var metadataPath = temporary + ".json";

        for (var attempt = 0; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                await TransferAsync(client ?? Client, uri, source.Headers, temporary, metadataPath, progress, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                File.Move(temporary, destination, overwrite: true);
                File.Delete(metadataPath);
                return destination;
            }
            catch (Exception error) when (attempt < MaxAttempts - 1
                && error is HttpRequestException or IOException)
            {
                // Cancellation is never retried; the partial file is preserved for a later resume.
                token.ThrowIfCancellationRequested();
                var backoff = TimeSpan.FromMilliseconds(500 * (1 << attempt));
                await Task.Delay(backoff, token).ConfigureAwait(false);
            }
        }
    }

    private static async Task TransferAsync(
        HttpClient client,
        Uri uri,
        IReadOnlyDictionary<string, string> headers,
        string temporary,
        string metadataPath,
        IProgress<AudioDownloadProgress>? progress,
        CancellationToken token)
    {
        var saved = File.Exists(temporary) ? new FileInfo(temporary).Length : 0;
        var previous = ReadMetadata(metadataPath);
        // HTTP Range without If-Range could silently stitch different audio files together.
        var canResume = saved > 0 && previous is not null &&
            (IsStrongTag(previous.ETag) || previous.LastModified.HasValue) &&
            (!previous.TotalBytes.HasValue || saved < previous.TotalBytes.Value);
        if (!canResume)
        {
            saved = 0;
            File.Delete(temporary);
            File.Delete(metadataPath);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        foreach (var header in headers)
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);

        if (canResume)
        {
            request.Headers.Range = new RangeHeaderValue(saved, null);
            if (IsStrongTag(previous!.ETag))
                request.Headers.IfRange = new RangeConditionHeaderValue(new EntityTagHeaderValue(previous.ETag!));
            else if (previous!.LastModified is { } lastModified)
                request.Headers.IfRange = new RangeConditionHeaderValue(lastModified);
        }

        using var headersTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        headersTimeout.CancelAfter(TimeSpan.FromSeconds(30));
        HttpResponseMessage received;
        try { received = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headersTimeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException error) when (!token.IsCancellationRequested)
        { throw new IOException("Servidor não respondeu por 30 segundos.", error); }
        using var response = received;
        if (canResume && response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            // The old partial cannot be verified against this origin; restart on the next attempt.
            File.Delete(temporary);
            File.Delete(metadataPath);
            throw new IOException("Intervalo de retomada rejeitado pelo servidor.");
        }
        response.EnsureSuccessStatusCode();
        var mediaType = response.Content.Headers.ContentType?.MediaType ?? "";
        if (mediaType.Contains("mpegurl", StringComparison.OrdinalIgnoreCase) ||
            mediaType.Contains("dash", StringComparison.OrdinalIgnoreCase) ||
            mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Playlist/manifesto exige download pelo extrator; stream direto indisponível.");

        if (canResume && response.StatusCode == HttpStatusCode.PartialContent)
        {
            var range = response.Content.Headers.ContentRange;
            var validatorMatches = IsStrongTag(previous!.ETag)
                ? string.Equals(response.Headers.ETag?.ToString(), previous.ETag, StringComparison.Ordinal)
                : previous.LastModified.HasValue && response.Content.Headers.LastModified == previous.LastModified;
            if (!validatorMatches || range?.From != saved || range?.HasLength != true ||
                previous.TotalBytes is { } oldLength && range.Length != oldLength)
            {
                File.Delete(temporary);
                File.Delete(metadataPath);
                throw new IOException("Resposta HTTP Range inconsistente; retomada descartada.");
            }
        }
        else if (response.StatusCode == HttpStatusCode.PartialContent)
        {
            throw new IOException("Servidor retornou conteúdo parcial sem intervalo solicitado.");
        }
        else
        {
            // A 200 response to a Range request means that the server declined to resume.
            saved = 0;
        }

        var totalBytes = response.StatusCode == HttpStatusCode.PartialContent
            ? response.Content.Headers.ContentRange!.Length
            : response.Content.Headers.ContentLength;
        var metadata = new PartialMetadata(
            response.Headers.ETag is { IsWeak: false } tag ? tag.ToString() : null,
            response.Content.Headers.LastModified, totalBytes);

        // Persist the validator before writing chunks; every restart revalidates the range.
        await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(metadata), token).ConfigureAwait(false);
        await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        await using var output = new FileStream(temporary, saved == 0 ? FileMode.Create : FileMode.Open,
            FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (saved > 0) output.Seek(0, SeekOrigin.End);

        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        var downloaded = saved;
        var lastBytes = downloaded;
        var lastReport = Stopwatch.GetTimestamp();
        progress?.Report(new(downloaded, totalBytes));
        try
        {
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(token);
            int count;
            while (true)
            {
                // A stalled origin must release the worker and reach the bounded retry path.
                idle.CancelAfter(TimeSpan.FromSeconds(30));
                try { count = await input.ReadAsync(buffer.AsMemory(0, BufferSize), idle.Token).ConfigureAwait(false); }
                catch (OperationCanceledException error) when (!token.IsCancellationRequested)
                { throw new IOException("Servidor não enviou áudio por 30 segundos.", error); }
                idle.CancelAfter(Timeout.InfiniteTimeSpan);
                if (count == 0) break;
                await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                downloaded += count;
                var elapsed = Stopwatch.GetElapsedTime(lastReport);
                if (elapsed < ProgressInterval) continue;
                progress?.Report(new(downloaded, totalBytes, (downloaded - lastBytes) / Math.Max(elapsed.TotalSeconds, 0.001)));
                lastBytes = downloaded;
                lastReport = Stopwatch.GetTimestamp();
            }
            await output.FlushAsync(token).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        if (downloaded == 0 || totalBytes is { } expected && downloaded != expected)
            throw new IOException("Download incompleto; arquivo parcial preservado para uma nova tentativa.");
        var tail = Stopwatch.GetElapsedTime(lastReport);
        progress?.Report(new(downloaded, totalBytes, (downloaded - lastBytes) / Math.Max(tail.TotalSeconds, 0.001)));
    }

    /// <summary>Explicitly discards only unfinished files for this track, without touching a completed download.</summary>
    public static void DiscardPartial(string directory, string stableKey)
    {
        if (!Directory.Exists(directory)) return;
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stableKey))).ToLowerInvariant();
        foreach (var extension in new[] { "m4a", "webm", "mp3", "ogg", "opus", "flac", "wav", "aac" })
        {
            var partial = Path.Combine(directory, identity + "." + extension + ".part");
            File.Delete(partial);
            File.Delete(partial + ".json");
        }
    }

    private static bool IsStrongTag(string? etag) =>
        !string.IsNullOrWhiteSpace(etag) && !etag.StartsWith("W/", StringComparison.OrdinalIgnoreCase);

    private static PartialMetadata? ReadMetadata(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<PartialMetadata>(File.ReadAllText(path)) : null;
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
