using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Hibike.Core.Services;

public static partial class AudioDownload
{
    private const int SegmentSize = 256 * 1024;
    private const int SegmentWorkers = 4;

    // ponytail: direct YouTube CDN only; manifest streams still require an extractor downloader.
    private static async Task<bool> TrySegmentedAsync(HttpClient client, Uri uri,
        IReadOnlyDictionary<string, string> headers, string temporary, string metadataPath,
        IProgress<AudioDownloadProgress>? progress, CancellationToken token)
    {
        HttpRequestMessage Request(long from, long to, PartialMetadata? version = null)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, uri);
            foreach (var header in headers) request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            request.Headers.Range = new RangeHeaderValue(from, to);
            if (TryGetStrongTag(version?.ETag, out var versionTag))
                request.Headers.IfRange = new RangeConditionHeaderValue(versionTag);
            else if (version?.LastModified is { } modified)
                request.Headers.IfRange = new RangeConditionHeaderValue(modified);
            return request;
        }

        using var probeTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        probeTimeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var probeRequest = Request(0, 0);
        HttpResponseMessage received;
        try { received = await client.SendAsync(probeRequest, HttpCompletionOption.ResponseHeadersRead, probeTimeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException error) when (!token.IsCancellationRequested)
        { throw new IOException("Tempo esgotado ao consultar blocos de áudio.", error); }
        using var probe = received;
        if (probe.StatusCode != HttpStatusCode.PartialContent) return false;
        var range = probe.Content.Headers.ContentRange;
        if (range?.From != 0 || range.To != 0 || range.Length is not > SegmentSize) return false;
        var total = range.Length.Value;
        var version = new PartialMetadata(TryGetStrongTag(probe.Headers.ETag, out var strongTag) ? strongTag.ToString() : null,
            probe.Content.Headers.LastModified, total);
        if (!IsStrongTag(version.ETag) && !version.LastModified.HasValue) return false;
        var mediaType = probe.Content.Headers.ContentType?.MediaType ?? "";
        if (mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
            || mediaType.Contains("mpegurl", StringComparison.OrdinalIgnoreCase)
            || mediaType.Contains("dash", StringComparison.OrdinalIgnoreCase)) return false;

        var previous = ReadMetadata(metadataPath);
        var saved = File.Exists(temporary) ? new FileInfo(temporary).Length : 0;
        if (previous != version || saved > total)
        {
            saved = 0;
            File.Delete(temporary);
        }
        await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(version), token).ConfigureAwait(false);
        await using var output = new FileStream(temporary, saved == 0 ? FileMode.Create : FileMode.Open,
            FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        output.Position = saved;
        progress?.Report(new(saved, total));
        PlaybackDiagnostics.Write("Download", "HTTP em blocos de 256 KiB; até quatro conexões por faixa.");

        while (saved < total)
        {
            var started = Stopwatch.GetTimestamp();
            using var batch = CancellationTokenSource.CreateLinkedTokenSource(token);
            var tasks = Enumerable.Range(0, SegmentWorkers)
                .Select(index => saved + (long)index * SegmentSize).Where(from => from < total)
                .Select(async from =>
                {
                    try
                    {
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(batch.Token);
                        timeout.CancelAfter(TimeSpan.FromSeconds(30));
                        var to = Math.Min(total - 1, from + SegmentSize - 1);
                        using var request = Request(from, to, version);
                        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                        var actual = response.Content.Headers.ContentRange;
                        var matches = IsStrongTag(version.ETag)
                            ? response.Headers.ETag?.ToString() == version.ETag
                            : response.Content.Headers.LastModified == version.LastModified;
                        if (response.StatusCode != HttpStatusCode.PartialContent || !matches
                            || actual?.From != from || actual.To != to || actual.Length != total)
                            throw new NotSupportedException("Servidor recusou blocos consistentes.");
                        var bytes = new byte[checked((int)(to - from + 1))];
                        await using var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
                        await input.ReadExactlyAsync(bytes, timeout.Token).ConfigureAwait(false);
                        var extra = new byte[1];
                        if (await input.ReadAsync(extra, timeout.Token).ConfigureAwait(false) != 0)
                            throw new NotSupportedException("Bloco excede intervalo solicitado.");
                        return bytes;
                    }
                    catch { batch.Cancel(); throw; }
                }).ToArray();
            byte[][] blocks;
            try { blocks = await Task.WhenAll(tasks).ConfigureAwait(false); }
            catch (Exception) when (!token.IsCancellationRequested
                && tasks.Any(task => task.Exception?.GetBaseException() is NotSupportedException))
            {
                output.SetLength(0);
                await output.FlushAsync(CancellationToken.None).ConfigureAwait(false);
                File.Delete(metadataPath);
                return false;
            }
            catch (OperationCanceledException error) when (!token.IsCancellationRequested)
            { throw new IOException("Tempo esgotado ao receber bloco de áudio.", error); }

            // Only complete, ordered batches reach disk: pause/retry retains a contiguous prefix.
            var before = saved;
            try
            {
                foreach (var bytes in blocks)
                    await output.WriteAsync(bytes, token).ConfigureAwait(false);
                await output.FlushAsync(token).ConfigureAwait(false);
            }
            catch
            {
                output.SetLength(before);
                throw;
            }
            saved += blocks.Sum(bytes => (long)bytes.Length);
            progress?.Report(new(saved, total, (saved - before) / Math.Max(.001, Stopwatch.GetElapsedTime(started).TotalSeconds)));
        }
        return true;
    }
}
