using Hibiki.Core.Models;
using Hibiki.Core.Services;
using Xunit;

namespace Hibiki.Core.Tests;

public class TrackTextFormatterTests
{
    [Theory]
    [InlineData("Artist - Song (Official Video)", "Artist - Topic", "Song")]
    [InlineData("artist — Song (official music video) (Lyrics)", "Artist", "Song")]
    [InlineData("Artist – Song [Copyright Free]", "Artist - TOPIC", "Song")]
    [InlineData("Another Artist - Song", "Artist", "Another Artist - Song")]
    [InlineData("Artist - ", "Artist", "Artist -")]
    [InlineData("Song (Live)", "Artist", "Song (Live)")]
    [InlineData("Song (Official Video) extra", "Artist", "Song (Official Video) extra")]
    public void CleansOnlyKnownSuffixesAndMatchingArtistPrefixes(string title, string artist, string expected) =>
        Assert.Equal(expected, TrackTextFormatter.Title(title, artist));

    [Theory]
    [InlineData("Artist - Topic", "Artist")]
    [InlineData("Artist - TOPIC ", "Artist")]
    [InlineData("My Topic", "My Topic")]
    public void CleansChannelSuffix(string artist, string expected) =>
        Assert.Equal(expected, TrackTextFormatter.Artist(artist));

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(267.49, "4:27")]
    [InlineData(267.5, "4:28")]
    [InlineData(59.5, "1:00")]
    [InlineData(3599.5, "1:00:00")]
    [InlineData(86400, "24:00:00")]
    public void DurationUsesConsistentRounding(double seconds, string expected)
    {
        Assert.Equal(expected, TrackTextFormatter.Duration(seconds));
        if (seconds > 0)
            Assert.Equal(expected, new Track("id", "title", "artist", "source", seconds).DurationLabel);
    }

    [Fact]
    public void UnknownAndInvalidDurationsAreSafe()
    {
        Assert.Equal("—", new Track("id", "title", "artist", "source").DurationLabel);
        Assert.Equal("—", TrackTextFormatter.Duration(double.NaN, true));
        Assert.Equal("0:00", TrackTextFormatter.Duration(double.PositiveInfinity));
    }
}
