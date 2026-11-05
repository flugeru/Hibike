using System.Net;
using System.Text;
using System.Text.Json;
using Hibike.Core.Services;
using Xunit;

namespace Hibike.Core.Tests;

public class WebAccountTests
{
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, true)]
    [InlineData(HttpStatusCode.Forbidden, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, false)]
    public async Task DistinguishesExpiredSessionFromTemporaryFailure(HttpStatusCode status, bool expired)
    {
        using var client = new HttpClient(new StatusHandler(status));
        using var account = YouTubeAccountLibrary.FromWebSession(client, "SAPISID=test-secret");
        var error = await Record.ExceptionAsync(() => account.ValidateSessionAsync(default));
        Assert.Equal(expired, error is AccountSessionExpiredException);
        Assert.Equal("SAPISID=test-secret", account.ExportWebSession());
        using var restored = YouTubeAccountLibrary.FromWebSession(client, account.ExportWebSession());
        Assert.Equal(account.ExportWebSession(), restored.ExportWebSession());
    }
    [Fact]
    public async Task TrackPageFetchesOnlyTheRequestedContinuation()
    {
        var handler = new Handler();
        using var http = new HttpClient(handler);
        using var library = YouTubeAccountLibrary.FromWebSession(http, "SAPISID=test-secret; SID=other");
        var first = await library.TrackPageAsync("PLtest", null, CancellationToken.None);
        Assert.Single(first.Tracks);
        Assert.Equal("next", first.Continuation);
        Assert.Equal(1, handler.TrackPages);
        var next = await library.TrackPageAsync("PLtest", first.Continuation, CancellationToken.None);
        Assert.Empty(next.Tracks);
        Assert.Null(next.Continuation);
        Assert.Equal(2, handler.TrackPages);
    }

    private sealed class StatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("{}") });
    }
    [Theory]
    [InlineData("https://accounts.google.com.br/signin", true)]
    [InlineData("https://accounts.google.co.uk/signin", true)]
    [InlineData("https://accounts.google.co.jp/signin", true)]
    [InlineData("https://accounts.google.de/signin", true)]
    [InlineData("https://music.youtube.com/", true)]
    [InlineData("https://accounts.google.com.br.evil.example/", false)]
    [InlineData("https://evilaccounts.google.com.br/", false)]
    [InlineData("https://accounts.google.invalid/", false)]
    [InlineData("https://accounts.google.com.br@evil.example/", false)]
    [InlineData("https://evil.example@accounts.google.com.br/", false)]
    [InlineData("https://accounts.google.com.br:8443/", false)]
    [InlineData("http://accounts.google.com.br/", false)]
    [InlineData("file:///private", false)]
    public void LoginNavigationChecksOfficialHosts(string url, bool expected)
        => Assert.Equal(expected, WebLoginNavigation.Allowed(url));

    [Fact] public async Task SessionAuthenticatesPaginatesAndDisconnects()
    {
        using var handler = new Handler();
        using var client = new HttpClient(handler);
        using var account = YouTubeAccountLibrary.FromWebSession(client, "SAPISID=test-secret; SID=other");
        await account.ValidateSessionAsync(default);
        var playlist = Assert.Single(await account.PlaylistsAsync(default));
        Assert.Equal("PLtest", playlist.Id); Assert.Equal("Minha lista", playlist.Title);
        var track = Assert.Single(await account.TracksAsync(playlist.Id, default));
        Assert.Equal("abcdefghijk", track.Id); Assert.Equal("Música", track.Title); Assert.Equal("Artista", track.Artist);
        Assert.Equal(2, handler.TrackPages);
        Assert.Equal("https://lh3.googleusercontent.com/music=w1000-h1000", track.Thumbnail);
        account.Dispose();
        await Assert.ThrowsAsync<InvalidOperationException>(() => account.PlaylistsAsync(default));
    }
    [Theory]
    [InlineData("SID=missing")]
    [InlineData("SAPISID=")]
    [InlineData("SAPISID=secret\r\nInjected: true")]
    public void RejectsInvalidCookies(string cookies) => Assert.Throws<InvalidOperationException>(() => YouTubeAccountLibrary.FromWebSession(new HttpClient(), cookies));

    private sealed class Handler : HttpMessageHandler
    {
        public int TrackPages;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal("music.youtube.com", request.RequestUri!.Host);
            Assert.Equal("https", request.RequestUri.Scheme);
            Assert.Equal("SAPISID=test-secret; SID=other", Assert.Single(request.Headers.GetValues("Cookie")));
            var authorization = Assert.Single(request.Headers.GetValues("Authorization"));
            Assert.StartsWith("SAPISIDHASH ", authorization);
            var time = authorization[12..].Split('_')[0];
            var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA1.HashData(Encoding.UTF8.GetBytes($"{time} test-secret https://music.youtube.com")));
            Assert.Equal($"SAPISIDHASH {time}_{hash}", authorization);
            string body;
            if (request.RequestUri.AbsolutePath.EndsWith("account_menu")) body = """{"activeAccountHeaderRenderer":{"accountName":{"runs":[{"text":"Conta"}]}}}""";
            else
            {
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                if (json.RootElement.GetProperty("browseId").GetString() == "FEmusic_liked_playlists")
                    body = """{"musicTwoRowItemRenderer":{"title":{"runs":[{"text":"Minha lista"}]},"navigationEndpoint":{"browseEndpoint":{"browseId":"VLPLtest"}}}}""";
                else
                {
                    TrackPages++;
                    Assert.Equal(TrackPages == 1 ? null : "next", json.RootElement.GetProperty("continuation").GetString());
                    body = TrackPages == 1
                        ? """{"musicResponsiveListItemRenderer":{"thumbnail":{"thumbnails":[{"url":"https://lh3.googleusercontent.com/music=w1000-h1000","width":1000},{"url":"https://lh3.googleusercontent.com/music=w60-h60","width":60}]},"flexColumns":[{"musicResponsiveListItemFlexColumnRenderer":{"text":{"runs":[{"text":"Música"}]}}},{"musicResponsiveListItemFlexColumnRenderer":{"text":{"runs":[{"text":"Artista"}]}}}],"playlistItemData":{"videoId":"abcdefghijk"}},"nextContinuationData":{"continuation":"next"}}"""
                        : "{}";
                }
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
