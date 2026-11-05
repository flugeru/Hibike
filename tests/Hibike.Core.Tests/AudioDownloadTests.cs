using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hibike.Core.Models;
using Hibike.Core.Services;
using Xunit;

public sealed class AudioDownloadTests
{
    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("valid-audio-bytes-for-download-test");

    [Fact]
    public async Task StreamsCompleteResponseToPublishedFile()
    {
        var directory = CreateDirectory();
        try
        {
            using var client = new HttpClient(new StubHandler(request =>
            {
                Assert.Null(request.Headers.Range);
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = new ByteArrayContent(Payload) };
                response.Headers.ETag = EntityTagHeaderValue.Parse("\"audio-v1\"");
                return response;
            }));
            var path = await AudioDownload.SaveAsync(Source(), directory, null, CancellationToken.None, "track-one", client);
            Assert.Equal(Payload, await File.ReadAllBytesAsync(path));
            Assert.False(File.Exists(path + ".part"));
            Assert.False(File.Exists(path + ".part.json"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task ResumesUsingValidatedRangeAndStrongEtag()
    {
        var directory = CreateDirectory();
        try
        {
            WritePartial(directory, "track-two", Payload[..8], "\"audio-v1\"", Payload.Length);
            using var client = new HttpClient(new StubHandler(request =>
            {
                Assert.Equal(8, request.Headers.Range?.Ranges.Single().From);
                Assert.Equal("\"audio-v1\"", request.Headers.IfRange?.EntityTag?.Tag);
                var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
                    { Content = new ByteArrayContent(Payload[8..]) };
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(8, Payload.Length - 1, Payload.Length);
                response.Headers.ETag = EntityTagHeaderValue.Parse("\"audio-v1\"");
                return response;
            }));
            var path = await AudioDownload.SaveAsync(Source(), directory, null, CancellationToken.None, "track-two", client);
            Assert.Equal(Payload, await File.ReadAllBytesAsync(path));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task RefusesToAppendUnvalidatedPartialFile()
    {
        var directory = CreateDirectory();
        try
        {
            WritePartial(directory, "track-three", Payload[..8], null, Payload.Length);
            using var client = new HttpClient(new StubHandler(request =>
            {
                Assert.Null(request.Headers.Range);
                return new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = new ByteArrayContent(Payload) };
            }));
            var path = await AudioDownload.SaveAsync(Source(), directory, null, CancellationToken.None, "track-three", client);
            Assert.Equal(Payload, await File.ReadAllBytesAsync(path));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task InvalidRangeIsDiscardedAndRetriedWithoutAppending()
    {
        var directory = CreateDirectory();
        try
        {
            WritePartial(directory, "track-four", Payload[..8], "\"audio-v1\"", Payload.Length);
            var attempts = 0;
            using var client = new HttpClient(new StubHandler(request =>
            {
                attempts++;
                if (request.Headers.Range is null)
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Payload) };
                var invalid = new HttpResponseMessage(HttpStatusCode.PartialContent)
                    { Content = new ByteArrayContent(Payload[8..]) };
                invalid.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, Payload.Length - 9, Payload.Length);
                return invalid;
            }));
            var path = await AudioDownload.SaveAsync(Source(), directory, null, CancellationToken.None, "track-four", client);
            Assert.Equal(2, attempts);
            Assert.Equal(Payload, await File.ReadAllBytesAsync(path));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void DiscardRemovesPartialsButKeepsCompletedFiles()
    {
        var directory = CreateDirectory();
        try
        {
            var destination = WritePartial(directory, "track-five", Payload[..8], "\"audio-v1\"", Payload.Length);
            File.WriteAllBytes(destination, Payload);
            AudioDownload.DiscardPartial(directory, "track-five");
            Assert.True(File.Exists(destination));
            Assert.False(File.Exists(destination + ".part"));
            Assert.False(File.Exists(destination + ".part.json"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task RejectsNonHttpsSourcesBeforeNetworkAccess()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AudioDownload.SaveAsync(new AudioSource("http://example.test/file.mp3", new Dictionary<string, string>(),
                Container: "mp3"), Path.GetTempPath(), null, CancellationToken.None));
        Assert.Contains("HTTPS", error.Message);
    }

    private static AudioSource Source() => new("https://example.test/audio", new Dictionary<string, string>(), Container: "mp3");

    [Fact]
    public async Task ReportsLengthBeforeFirstReadAndHonorsCancellationWithoutRetry()
    {
        var directory = CreateDirectory();
        try
        {
            using var cancellation = new CancellationTokenSource();
            var requests = 0;
            using var client = new HttpClient(new StubHandler(_ =>
            {
                requests++;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Payload) };
            }));
            var progress = new ImmediateProgress(update =>
            {
                Assert.Equal(0, update.Bytes);
                Assert.Equal(Payload.Length, update.TotalBytes);
                cancellation.Cancel();
            });
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                AudioDownload.SaveAsync(Source(), directory, progress, cancellation.Token, "cancel-test", client));
            Assert.Equal(1, requests);
            Assert.Empty(Directory.GetFiles(directory, "*.mp3"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private sealed class ImmediateProgress(Action<AudioDownloadProgress> report) : IProgress<AudioDownloadProgress>
    {
        public void Report(AudioDownloadProgress value) => report(value);
    }

    private static string CreateDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "hibike-audio-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string WritePartial(string directory, string key, byte[] bytes, string? etag, int total)
    {
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
        var destination = Path.Combine(directory, digest + ".mp3");
        File.WriteAllBytes(destination + ".part", bytes);
        File.WriteAllText(destination + ".part.json", JsonSerializer.Serialize(new
        {
            ETag = etag,
            LastModified = (DateTimeOffset?)null,
            TotalBytes = (long?)total
        }));
        return destination;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }
}
