using Hibiki.Core.Services;
using Xunit;

namespace Hibiki.Core.Tests;

public sealed class TrackTextFormatterTests
{
    [Theory]
    [InlineData("Lorien Testard - Our Drafts Unite (Official Music Video)", "Lorien Testard - Topic", "Our Drafts Unite")]
    [InlineData("Artist — Song (official video)", "Artist", "Song")]
    [InlineData("Artist: Song [Copyright Free]", "Artist", "Song")]
    [InlineData("Track (Lyrics) (Official Video)", "Channel", "Track")]
    [InlineData("Clair Obscur - Our Drafts Unite", "Unrelated Channel", "Clair Obscur - Our Drafts Unite")]
    [InlineData("Mask Keeper and Visages Boss Theme", "Finding Purposes", "Mask Keeper and Visages Boss Theme")]
    public void CleansDisplayTitlesWithoutDamagingUnknownSongs(string raw, string artist, string expected) =>
        Assert.Equal(expected, TrackTextFormatter.Title(raw, artist));

    [Theory]
    [InlineData("Lorien Testard - Topic", "Lorien Testard")]
    [InlineData("Lorien Testard – Topic", "Lorien Testard")]
    [InlineData("Artist", "Artist")]
    public void RemovesTopicSuffix(string artist, string expected) =>
        Assert.Equal(expected, TrackTextFormatter.Artist(artist));

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(125, "2:05")]
    [InlineData(267.49, "4:27")]
    [InlineData(267.5, "4:28")]
    [InlineData(3600, "1:00:00")]
    [InlineData(3823.9, "1:03:44")]
    public void DurationUsesOneRoundingRule(double seconds, string expected) =>
        Assert.Equal(expected, TrackTextFormatter.Duration(seconds));

    [Fact]
    public void MissingDurationCanShowDashInLists() =>
        Assert.Equal("—", TrackTextFormatter.Duration(0, unknownAsDash: true));

    [Fact]
    public void FormatterDoesNotChangeTrackMetadata()
    {
        var track = new Hibiki.Core.Models.Track("id", "Singer - Song (Lyrics)", "Singer - Topic", "https://example.org", 267.5);
        Assert.Equal("Song", TrackTextFormatter.Title(track.Title, track.Artist));
        Assert.Equal("Singer", TrackTextFormatter.Artist(track.Artist));
        Assert.Equal("Singer - Song (Lyrics)", track.Title);
        Assert.Equal("4:28", track.DurationLabel);
    }
}
