using Hibike.Core.Services;
using Xunit;

namespace Hibike.Core.Tests;

public sealed class RadioAndLyricsTests
{
    [Fact]
    public void RadioContinuationIsTakenFromTheQueuePanel()
    {
        const string json = """
            {"elsewhere":{"continuation":"not-a-radio-token"},
             "continuationContents":{"playlistPanelContinuation":{
              "contents":[{"playlistPanelVideoRenderer":{"videoId":"abcdefghijk","title":{"simpleText":"Song"}}}],
              "continuations":[{"nextContinuationData":{"continuation":"page-2"}}]
            }}}
            """;
        var page = YouTubeMusicRadio.ParsePage(json);
        Assert.Equal("page-2", page.Continuation);
        Assert.Equal("abcdefghijk", Assert.Single(page.Tracks).Id);
    }

    [Fact]
    public void LyricsParserPreservesRepeatedTimestampsAndSortsLines()
    {
        var lines = SongLyrics.Parse("[offset:100]\n[00:05.20][00:02.50]Repeat\n[00:01.23]First");
        Assert.Collection(lines,
            line => { Assert.Equal(1.13, line.Seconds, 2); Assert.Equal("First", line.Text); },
            line => { Assert.Equal(2.4, line.Seconds, 2); Assert.Equal("Repeat", line.Text); },
            line => Assert.Equal(5.1, line.Seconds, 2));
        Assert.Empty(SongLyrics.Parse("A lyric without timestamps"));
    }
}
