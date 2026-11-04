using Hibike.Core.Models;
using Hibike.Core.Services;
using Xunit;

namespace Hibike.Core.Tests;

public class MusicDiscoveryTests
{
    [Fact]
    public void ArtistProfileSeparatesSongsAlbumsAndDiscography()
    {
        var profile = MusicDiscovery.ParseArtist("""
            {"microformatDataRenderer":{"title":"Artist","thumbnail":{"thumbnails":[{"url":"https://example.org/photo.jpg"}]}},
             "items":[{"musicResponsiveListItemRenderer":{"flexColumns":[{"musicResponsiveListItemFlexColumnRenderer":{"text":{"runs":[{"text":"Song"}]}}}],"watchEndpoint":{"videoId":"song"}}},
             {"musicTwoRowItemRenderer":{"title":{"runs":[{"text":"Album"}]},"browseEndpoint":{"browseId":"MPREalbum"},"watchEndpoint":{"videoId":"preview"}}}],
             "musicCarouselShelfBasicHeaderRenderer":{"title":{"runs":[{"text":"Albums"}]},"moreContentButton":{"buttonRenderer":{"browseEndpoint":{"browseId":"MPADartist","params":"page"}}}}}
            """, "UCabcdefghijklmnopqrstuv");
        Assert.Equal("https://example.org/photo.jpg", profile.Image);
        Assert.Equal("Song", Assert.Single(profile.Tracks).Title);
        Assert.Equal("Álbum", Assert.Single(profile.Collections).Kind);
        Assert.Equal("MPADartist", Assert.Single(profile.Sections).Id);
    }
    [Fact]
    public void ParsesRealArtistIdentityNotTrackUploader()
    {
        var json = """{"contents":[{"musicResponsiveListItemRenderer":{"flexColumns":[{"musicResponsiveListItemFlexColumnRenderer":{"text":{"runs":[{"text":"Artist"}]}}}],"navigationEndpoint":{"browseEndpoint":{"browseId":"UCabcdefghijklmnopqrstuv"}},"thumbnails":[{"url":"https://example.org/artist.jpg"}]}},{"musicResponsiveListItemRenderer":{"flexColumns":[{"musicResponsiveListItemFlexColumnRenderer":{"text":{"runs":[{"text":"Song"}]}}}],"watchEndpoint":{"videoId":"abcdefghijk"}}}]}""";
        var artist = Assert.Single(MusicDiscovery.ParseArtists(json));
        Assert.Equal("Artist", artist.Name); Assert.Equal("UCabcdefghijklmnopqrstuv", artist.Id);
        Assert.Equal("https://example.org/artist.jpg", artist.Image);
        Assert.Empty(MusicDiscovery.ParseArtists("{}"));
    }
    [Fact]
    public void RankingHonorsFeedbackDiversityAndArtistPreference()
    {
        var tracks = Enumerable.Range(0, 12).Select(i => new Track(i.ToString(), "Song " + i, i < 8 ? "Favorite" : "Discovery", "https://example.org/" + i)).ToArray();
        var ranked = MusicRecommendations.Rank(tracks.Concat(tracks), new HashSet<string> { "Favorite" }, [], [], new HashSet<string> { tracks[0].Source }, _ => 0, new());
        Assert.Equal("Favorite", ranked[0].Artist); Assert.DoesNotContain(ranked, t => t.Source == tracks[0].Source);
        Assert.All(ranked.GroupBy(t => t.Artist), g => Assert.True(g.Count() <= 3));
        Assert.Equal(ranked.Count, ranked.Select(t => t.Source).Distinct().Count());
        Assert.Empty(MusicRecommendations.Rank([], new HashSet<string>(), [], [], new HashSet<string>(), _ => 0, new()));
    }
    [Fact]
    public void InvalidWeightsRejected() => Assert.Throws<ArgumentException>(() => MusicRecommendations.Rank([], new HashSet<string>(), [], [], new HashSet<string>(), _ => 0, new(Artists: double.NaN)));
}
