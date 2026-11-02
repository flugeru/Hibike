using Hibike.Core.Models;
using Hibike.Core.Services;
using Xunit;

namespace Hibike.Core.Tests;

public class PlaybackSourcesTests
{
    private static readonly Track Original = new("one", "Song (Official Audio)", "Artist - Topic", "https://youtube.com/watch?v=one", 200);
    [Theory]
    [InlineData("Song", "Artist", 201, true)]
    [InlineData("Artist — Song [Official Audio]", "Music Uploads", 206, true)]
    [InlineData("Song Official Audio", "Artist", 204, true)]
    [InlineData("Artist - Song (Cover)", "Music Uploads", 200, false)]
    [InlineData("Other Artist - Song", "Music Uploads", 200, false)]
    [InlineData("Song (Cover)", "Artist", 200, false)]
    [InlineData("Song (Live)", "Artist", 200, false)]
    [InlineData("Song", "Other Artist", 200, false)]
    [InlineData("Song", "Artist", 210, false)]
    [InlineData("Song", "Artist", 0, false)]
    public void MatchesConservatively(string title, string artist, double duration, bool expected) =>
        Assert.Equal(expected, PlaybackSources.IsProbableMatch(Original, new("two", title, artist, "https://soundcloud.com/artist/song", duration)));

    [Fact] public async Task FallsBackWithoutReplacingQueueIdentity()
    {
        var catalog = new Catalog();
        var resolved = await PlaybackSources.ResolveFirstAsync(catalog, Original, default);
        Assert.Equal("SoundCloud", PlaybackSources.Provider(resolved.Track));
        Assert.Equal("https://example.org/audio", resolved.Audio.Url);
    }
    [Fact] public async Task KeepsOriginalWhenItResolvesFirst()
    {
        var track = Original with { Source = "https://example.org/original" };
        var resolved = await PlaybackSources.ResolveFirstAsync(new Catalog(), track, default);
        Assert.Equal(track, resolved.Track);
    }
    [Fact] public async Task HonorsCancellation()
    {
        using var stop = new CancellationTokenSource(); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PlaybackSources.ResolveFirstAsync(new Catalog(), Original, stop.Token));
    }
    private sealed class Catalog : IMusicCatalog
    {
        public Task<IReadOnlyList<Track>> SearchAsync(string query, CancellationToken token) => Task.FromResult<IReadOnlyList<Track>>(
            [new("two", "Song", "Artist", "https://soundcloud.com/artist/song", 200)]);
        public Task<AudioSource> ResolveAsync(Track track, CancellationToken token) => PlaybackSources.Provider(track) == "YouTube"
            ? Task.FromException<AudioSource>(new IOException("Original unavailable"))
            : Task.FromResult(new AudioSource("https://example.org/audio", new Dictionary<string, string>()));
    }
}
