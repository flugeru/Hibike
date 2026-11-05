using Hibike.Core.Models;
using Hibike.Core.Services;
using Xunit;
namespace Hibike.Core.Tests;

public class CatalogTests
{
    [Fact] public async Task ExperimentalSearchLimitsPlaylists()
    {
        var runner = new FakeRunner("{\"entries\":[]}");
        await new YtDlpCatalog(runner).SearchAsync("scsearch30:artist", default);
        Assert.Equal("scsearch30:artist", runner.Arguments[^1]);
        Assert.Contains("--yes-playlist", runner.Arguments);
        Assert.Contains("--playlist-end", runner.Arguments);
    }
    [Fact] public async Task ExperimentalDownloadRejectsLocalPaths()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => AudioDownload.SaveAsync(new("file:///private.flac", new Dictionary<string, string>()), "unused", null, default));
    }
    [Fact] public void PlaybackSessionPreservesQueueAndPosition()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var store = new PlaybackSessionStore(directory);
            var track = new Track("one", "Song", "Artist", "https://example.org/song");
            store.Save(new([track, track with { Id = "two" }], track, 0, 123.75, [track]));
            var saved = store.Load()!;
            Assert.Equal(123.75, saved.Position); Assert.Equal(track, saved.Current); Assert.Equal(2, saved.Queue.Length);
            Assert.Equal(track, Assert.Single(saved.History!));
            store.Save(new([track], track, 0, 124));
            Assert.Single(store.Load()!.Queue);
            Assert.Null(store.Load()!.History);
        }
        finally { Directory.Delete(directory, true); }
    }
    [Fact] public void SearchHandlesNullEntriesAndDeduplicates()
    {
        var tracks = YtDlpCatalog.ParseSearch("""{"entries":[null,{"id":"abc","title":"Song","duration":125,"url":"abc"},{"id":"abc","title":"Song"}]}""");
        var track = Assert.Single(tracks);
        Assert.Equal("https://www.youtube.com/watch?v=abc", track.Source);
        Assert.Equal("2:05", track.DurationLabel);
    }
    [Fact] public async Task SearchTreatsOptionsAsText()
    {
        var runner = new FakeRunner("""{"entries":[]}""");
        await new YtDlpCatalog(runner).SearchAsync("--exec bad", default);
        Assert.Equal("--", runner.Arguments[^2]);
        Assert.Equal("ytsearch30:--exec bad", runner.Arguments[^1]);
    }
    [Fact] public async Task ResolvePreservesPlaybackHeaders()
    {
        var runner = new FakeRunner("""{"url":"https://example.org/audio","http_headers":{"User-Agent":"Hibike","Referer":"https://example.org"}}""");
        var source = await new YtDlpCatalog(runner).ResolveAsync(new("id","Song","Artist","https://example.org"), default);
        Assert.Equal("Hibike", source.Headers["User-Agent"]);
    }
    [Fact] public async Task ResolveSelectsBestAudioAndPreservesQualityMetadata()
    {
        var runner = new FakeRunner("""{"url":"https://example.org/audio","abr":159.7,"acodec":"opus","asr":48000,"audio_channels":2,"format_id":"251","ext":"webm"}""");
        var source = await new YtDlpCatalog(runner).ResolveAsync(new("id", "Song", "Artist", "https://example.org"), default);
        Assert.Equal("bestaudio", runner.Arguments[runner.Arguments.ToList().IndexOf("-f") + 1]);
        Assert.Equal(159.7, source.BitrateKbps);
        Assert.Equal("opus", source.Codec);
        Assert.Equal(48000, source.SampleRateHz);
        Assert.Equal(2, source.Channels);
        Assert.Equal("251", source.Format);
        Assert.Equal("webm", source.Container);
    }
    [Fact] public async Task FavoritesRoundTripAtomically()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var store = new LibraryStore(path);
            var track = new Track("a","My song","Artist","https://example.org/a");
            await store.SaveAsync([track]);
            Assert.Equal(track, Assert.Single(await store.LoadAsync()));
            await store.SaveAsync([]);
            Assert.Empty(await store.LoadAsync());
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }
    [Fact] public async Task FailedExtractionSurfacesError()
    {
        var runner = new FakeRunner("") { ExitCode = 1 };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new YtDlpCatalog(runner).SearchAsync("music", default));
    }
    [Fact] public async Task CatalogUsesPrivateCacheWithoutImportedSession()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            Directory.CreateDirectory(directory);
            var cookies = Path.Combine(directory, "youtube-cookies.txt");
            await File.WriteAllTextAsync(cookies, "# Netscape HTTP Cookie File");
            var runner = new FakeRunner("{\"entries\":[]}");
            await new YtDlpCatalog(runner, directory).SearchAsync("music", default);
            Assert.Contains("--cache-dir", runner.Arguments);
            Assert.DoesNotContain(cookies, runner.Arguments);
            Assert.DoesNotContain("--cookies", runner.Arguments);
            runner = new FakeRunner("{\"url\":\"https://example.org/audio\"}");
            await new YtDlpCatalog(runner, directory).ResolveAsync(new("id", "Song", "Artist", "https://www.youtube.com/watch?v=id"), default);
            Assert.DoesNotContain(cookies, runner.Arguments);
            Assert.DoesNotContain("--cookies", runner.Arguments);
            Assert.DoesNotContain("--no-cache-dir", runner.Arguments);
        }
        finally { Directory.Delete(directory, true); }
    }
    [Fact] public void DiagnosticsDoNotRetainSignedUrls()
    {
        PlaybackDiagnostics.Write("test", "failed https://example.org/audio?token=secret");
        Assert.DoesNotContain("token=secret", PlaybackDiagnostics.Snapshot());
    }
    [Fact]
    public void DiagnosticSectorsHaveIndependentHistoryAndOmitCredentials()
    {
        var marker = Guid.NewGuid().ToString("N");
        PlaybackDiagnostics.Write(DiagnosticSector.Google, marker,
            "HTTP 401\nAuthorization: Bearer private-bearer\nCookie: SAPISID=private-cookie");
        PlaybackDiagnostics.Write(DiagnosticSector.Discord, marker,
            "Conta dev@example.org: abcdefghijklmnopqrstuv.123456.abcdefghijklmnopqrstuvwxyz");
        PlaybackDiagnostics.WriteFailure(DiagnosticSector.Google, "Login",
            new HttpRequestException("Provider body: private-provider-detail", null, System.Net.HttpStatusCode.Forbidden));
        for (var i = 0; i < 100; i++)
            PlaybackDiagnostics.Write(DiagnosticSector.Playback, "Player", $"Reprodução {i}.");

        PlaybackDiagnostics.Write("Metadados locais", "Não abriu '/home/private-folder/My song.flac'.");
        var google = PlaybackDiagnostics.Capture(DiagnosticSector.Google);
        Assert.Contains(google, entry => entry.Stage == marker && entry.Message.Contains("HTTP 401"));
        Assert.Contains(google, entry => entry.Stage == "Login" && entry.Message.Contains("HTTP 403"));
        Assert.All(google, entry => Assert.Equal(DiagnosticSector.Google, entry.Sector));
        Assert.Equal(80, PlaybackDiagnostics.Capture(DiagnosticSector.Playback).Count);
        var copy = PlaybackDiagnostics.Snapshot();
        Assert.DoesNotContain("private-bearer", copy);
        Assert.DoesNotContain("private-cookie", copy);
        Assert.DoesNotContain("private-provider-detail", copy);
        Assert.DoesNotContain("dev@example.org", copy);
        Assert.DoesNotContain("private-folder", copy);
        Assert.DoesNotContain("abcdefghijklmnopqrstuv.123456.abcdefghijklmnopqrstuvwxyz", copy);
    }

    [Fact] public async Task CachedResolutionAvoidsDuplicateExtraction()
    {
        var runner = new FakeRunner("{\"url\":\"https://example.org/audio\"}");
        var catalog = new CachedMusicCatalog(new YtDlpCatalog(runner));
        var track = new Track("id", "Title", "Artist", "https://example.org/video");
        var first = await catalog.ResolveAsync(track, default);
        var second = await catalog.ResolveAsync(track, default);
        Assert.Same(first, second);
        Assert.Equal(1, runner.Calls);
        Assert.Contains("--skip-download", runner.Arguments);
    }
    [Fact] public async Task CachedResolutionRetriesAfterSynchronousFailure()
    {
        var runner = new FakeRunner("not json");
        var catalog = new CachedMusicCatalog(new YtDlpCatalog(runner));
        var track = new Track("id", "Title", "Artist", "https://example.org/video");
        await Assert.ThrowsAnyAsync<System.Text.Json.JsonException>(() => catalog.ResolveAsync(track, default));
        await Assert.ThrowsAnyAsync<System.Text.Json.JsonException>(() => catalog.ResolveAsync(track, default));
        Assert.Equal(2, runner.Calls);
    }
    [Fact] public async Task CachedResolutionHonorsCanceledRequests()
    {
        var runner = new FakeRunner("{\"url\":\"https://example.org/audio\"}");
        var catalog = new CachedMusicCatalog(new YtDlpCatalog(runner));
        var track = new Track("id", "Title", "Artist", "https://example.org/video");
        await catalog.ResolveAsync(track, default);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => catalog.ResolveAsync(track, cancellation.Token));
        Assert.Equal(1, runner.Calls);
    }
    [Fact] public void PlaybackSessionRejectsInvalidCurrentTrack()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var store = new PlaybackSessionStore(directory);
            store.Save(new([], new Track("id", "Title", "Artist", ""), -1, 0));
            Assert.Throws<InvalidDataException>(() => store.Load());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Theory]
    [InlineData("[null]")]
    [InlineData("[{\"Id\":\"id\",\"Title\":\"Song\",\"Artist\":\"Artist\"}]")]
    public async Task FavoritesRejectInvalidTracksWithoutChangingFile(string json)
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "favorites.json");
            await File.WriteAllTextAsync(path, json);
            await Assert.ThrowsAsync<InvalidDataException>(() => new LibraryStore(directory).LoadAsync());
            Assert.Equal(json, await File.ReadAllTextAsync(path));
        }
        finally { Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task BrowsePagesBeyondHundredAndRejectsForeignHost()
    {
        var runner = new FakeRunner("{\"entries\":[]}");
        var catalog = new CachedMusicCatalog(new YtDlpCatalog(runner));
        await catalog.BrowsePageAsync("https://www.youtube.com/channel/UCabcdefghijklmnopqrstuv/videos", 2, CancellationToken.None);
        var args = runner.Arguments.ToList();
        Assert.Equal("101", args[args.IndexOf("--playlist-start") + 1]);
        Assert.Equal("150", args[args.IndexOf("--playlist-end") + 1]);
        await Assert.ThrowsAsync<ArgumentException>(() => catalog.BrowsePageAsync("https://youtube.com.evil.test/channel/test", 0, CancellationToken.None));
        Assert.Equal(1, runner.Calls);
    }
    private sealed class FakeRunner(string output) : ICommandRunner
    {
        public IReadOnlyList<string> Arguments { get; private set; } = [];
        public int ExitCode { get; init; }
        public int Calls { get; private set; }
        public Task<ProcessResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        { Calls++; Arguments = arguments; return Task.FromResult(new ProcessResult(ExitCode, output, "test error")); }
    }
}
