using System.Net;
using System.Text;
using Hibike.Core.Services;
using Xunit;

namespace Hibike.Core.Tests;

public class AccountLibraryTests
{
    [Fact] public async Task ReadsPagesWithoutLeakingTokenToUrl()
    {
        using var client = new HttpClient(new Handler());
        var library = new YouTubeAccountLibrary(client, "test-token");
        var lists = await library.PlaylistsAsync(default);
        Assert.Equal(2, lists.Count);
        var track = Assert.Single(await library.TracksAsync("private-list", default));
        Assert.Equal("https://www.youtube.com/watch?v=abc", track.Source);
        Assert.Equal("Artist", track.Artist);
        Assert.Single(await library.TracksAsync(null, default));
    }

    [Fact] public async Task RejectsExpiredSessionWithoutExposingResponse()
    {
        using var client = new HttpClient(new Handler(true));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new YouTubeAccountLibrary(client, "test-token").PlaylistsAsync(default));
        Assert.Contains("401", error.Message);
        Assert.DoesNotContain("test-token", error.Message);
    }

    private sealed class Handler(bool expired = false) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal("https", request.RequestUri!.Scheme);
            Assert.Equal("www.googleapis.com", request.RequestUri.Host);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("test-token", request.Headers.Authorization.Parameter);
            Assert.DoesNotContain("test-token", request.RequestUri.ToString());
            if (expired) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            var json = request.RequestUri.AbsolutePath.EndsWith("playlists")
                ? request.RequestUri.Query.Contains("pageToken=")
                    ? """{"items":[{"id":"two","snippet":{"title":"Second"}}]}"""
                    : """{"items":[{"id":"one","snippet":{"title":"First"}}],"nextPageToken":"page-two"}"""
                : request.RequestUri.AbsolutePath.EndsWith("playlistItems")
                    ? """{"items":[{"snippet":{"title":"Song","videoOwnerChannelTitle":"Artist","resourceId":{"videoId":"abc"}}},{"snippet":{"title":"Private video","resourceId":{"videoId":"hidden"}}}]}"""
                    : """{"items":[{"id":"liked","snippet":{"title":"Liked song","channelTitle":"Artist"}}]}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
}
