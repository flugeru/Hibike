using Hibiki.Core.Models;
using Hibiki.Core.Services;
using Xunit;
namespace Hibiki.Core.Tests;

public class CatalogTests
{
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
        Assert.Equal("ytsearch15:--exec bad", runner.Arguments[^1]);
    }
    [Fact] public async Task ResolvePreservesPlaybackHeaders()
    {
        var runner = new FakeRunner("""{"url":"https://example.org/audio","http_headers":{"User-Agent":"Hibiki","Referer":"https://example.org"}}""");
        var source = await new YtDlpCatalog(runner).ResolveAsync(new("id","Song","Artist","https://example.org"), default);
        Assert.Equal("Hibiki", source.Headers["User-Agent"]);
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
    [Fact] public async Task CatalogUsesPrivateCacheAndImportedSession()
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
            Assert.Contains(cookies, runner.Arguments);
            Assert.DoesNotContain("--no-cache-dir", runner.Arguments);
        }
        finally { Directory.Delete(directory, true); }
    }
    [Fact] public void DiagnosticsDoNotRetainSignedUrls()
    {
        PlaybackDiagnostics.Write("test", "failed https://example.org/audio?token=secret");
        Assert.DoesNotContain("token=secret", PlaybackDiagnostics.Snapshot());
    }
    private sealed class FakeRunner(string output) : ICommandRunner
    {
        public IReadOnlyList<string> Arguments { get; private set; } = [];
        public int ExitCode { get; init; }
        public Task<ProcessResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        { Arguments = arguments; return Task.FromResult(new ProcessResult(ExitCode, output, "test error")); }
    }
}
