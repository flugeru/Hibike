using Hibike.Core.Models;
using Xunit;

namespace Hibike.Core.Tests;

public class TrackTextFormatterTests
{
    [Theory]
    [InlineData("Artist - Song (Official Video)", "Artist - Topic", "Song")]
    [InlineData("artist - Song (official video)", "Artist", "Song")]
    [InlineData("Artist – Song (Official Music Video)", "Artist", "Song")]
    [InlineData("Song [Copyright Free] (Lyrics)", "Artist", "Song")]
    [InlineData("Other - Song", "Artist", "Other - Song")]
    [InlineData("Artist - Song (Live)", "Artist", "Song (Live)")]
    [InlineData("Artist - ", "Artist", "Artist -")]
    [InlineData(null, null, "")]
    public void CleansOnlyKnownNoise(string? title, string? artist, string expected) =>
        Assert.Equal(expected, TrackTextFormatter.Title(title, artist));

    [Theory]
    [InlineData(267.8, "4:28")]
    [InlineData(268, "4:28")]
    [InlineData(59.5, "1:00")]
    [InlineData(3599.9, "1:00:00")]
    [InlineData(360000, "100:00:00")]
    [InlineData(-1, "0:00")]
    [InlineData(double.NaN, "0:00")]
    [InlineData(double.PositiveInfinity, "0:00")]
    public void UsesSameRoundingEverywhere(double seconds, string expected) =>
        Assert.Equal(expected, TrackTextFormatter.Duration(seconds));

    [Fact]
    public void CleansChannelAndSharesTrackDuration()
    {
        Assert.Equal("Artist", TrackTextFormatter.Artist("Artist - Topic"));
        Assert.Equal("Topic Artist", TrackTextFormatter.Artist("Topic Artist"));
        Assert.Equal(TrackTextFormatter.Duration(267.8), new Track("id", "Song", "Artist", "url", 267.8).DurationLabel);
    }
}
