using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Hibike.Core.Models;

namespace Hibike.Core.Services;

public static partial class SongLyrics
{
    private sealed record Result(string? PlainLyrics, string? SyncedLyrics, bool Instrumental);
    public sealed record Line(double Seconds, string Text);
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(10), MaxResponseContentBufferSize = 256_000 };

    public static async Task<string?> LoadAsync(Track track, string directory, CancellationToken token)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(track.Source)));
        var path = Path.Combine(directory, key + ".lrc");
        if (File.Exists(path)) return await File.ReadAllTextAsync(path, token);
        var artist = track.MusicArtist ?? TrackTextFormatter.Artist(track.Artist);
        var title = track.MusicTitle ?? TrackTextFormatter.Title(track.Title, track.Artist);
        var query = $"track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}";
        if (track.Album is { Length: > 0 } album) query += "&album_name=" + Uri.EscapeDataString(album);
        if (track.DurationSeconds > 0) query += $"&duration={(int)Math.Round(track.DurationSeconds)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://lrclib.net/api/get?" + query);
        request.Headers.UserAgent.ParseAdd("Hibike (https://github.com/flugeru/Hibike)");
        using var response = await Client.SendAsync(request, token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<Result>(cancellationToken: token);
        var lyrics = result?.SyncedLyrics ?? result?.PlainLyrics ?? (result?.Instrumental == true ? "Faixa instrumental" : null);
        if (string.IsNullOrWhiteSpace(lyrics)) return null;
        Directory.CreateDirectory(directory);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, lyrics, token);
            File.Move(temp, path, true);
        }
        finally { File.Delete(temp); }
        return lyrics;
    }

    [GeneratedRegex(@"\[(\d{1,3}):([0-5]\d(?:[.,]\d{1,3})?)\]")]
    private static partial Regex Timestamp();

    [GeneratedRegex(@"\[offset:([+-]?\d+)\]", RegexOptions.IgnoreCase)]
    private static partial Regex Offset();

    public static IReadOnlyList<Line> Parse(string lyrics)
    {
        var offset = int.TryParse(Offset().Match(lyrics).Groups[1].Value, out var value) ? value / 1000d : 0;
        var lines = new List<Line>();
        foreach (var line in lyrics.Split('\n'))
        {
            var content = Timestamp().Replace(line, "").Trim();
            foreach (Match match in Timestamp().Matches(line))
            {
                var seconds = double.Parse(match.Groups[2].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
                lines.Add(new(Math.Max(0, int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) * 60 + seconds - offset), content));
            }
        }
        return lines.OrderBy(line => line.Seconds).ToArray();
    }
}
