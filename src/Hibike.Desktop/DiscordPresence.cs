using System.Text;
using System.Text.Json.Serialization;
using Hibike.App;
using Hibike.Core.Models;
using Hibike.Core.Services;

namespace Hibike.Desktop;

public sealed class DiscordPresence : IDisposable
{
    public const string ApplicationId = "1566415890596241459";
    private readonly MusicController _music;
    private readonly DiscordIpcClient _client = new();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private readonly CancellationTokenSource _stop = new();
    private readonly Dictionary<string, string?> _covers = new();
    private bool _busy, _disposed, _visible;
    private string? _lastKey;
    private double _lastPosition;
    private DateTime _lastUpdate;
    private DateTime _retryAt;

    public DiscordPresence(MusicController music)
    {
        _music = music;
        music.Changed += Update; music.Tick += Update;
        Update();
    }

    private async void Update()
    {
        if (_busy || _disposed || DateTime.UtcNow < _retryAt) return;
        _busy = true;
        try
        {
            var track = _music.Current;
            if (track is null || !_music.Player.IsPlaying || _music.LoadingAudio)
            {
                if (_visible) await _client.SetActivityAsync(null);
                _visible = false; _lastKey = null; return;
            }
            var position = _music.Player.Position;
            var now = DateTime.UtcNow;
            var cover = PublicArtwork(_music.CurrentSource?.Artwork ?? track.Thumbnail);
            var key = track.Source + "\n" + track.Title + "\n" + track.Artist + "\n" + track.Album
                + "\n" + cover;
            if (_visible && key == _lastKey && Math.Abs(position - _lastPosition - (now - _lastUpdate).TotalSeconds) < 3) return;
            if (cover is null && track.IsLocal) cover = await FindCoverAsync(track);
            if (_disposed || _music.Current != track || !_music.Player.IsPlaying || _music.LoadingAudio) return;
            position = _music.Player.Position; now = DateTime.UtcNow;
            var duration = _music.Player.Duration > 0 ? _music.Player.Duration : track.DurationSeconds;
            await _client.SetActivityAsync(Build(track, position, duration, cover, now));
            _visible = true; _lastKey = key; _lastPosition = position; _lastUpdate = now;
            _retryAt = DateTime.MinValue;
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception error)
        {
            _visible = false;
            _lastKey = null;
            _retryAt = DateTime.UtcNow.AddSeconds(10);
            PlaybackDiagnostics.Write("Discord", error.Message);
        }
        finally { _busy = false; }
    }

    // Retain best-effort iTunes album art for local files without publicly accessible artwork.
    private async Task<string?> FindCoverAsync(Track track)
    {
        if (string.IsNullOrWhiteSpace(track.Album) || string.IsNullOrWhiteSpace(track.Artist)) return null;
        var key = track.Artist + "\n" + track.Album;
        if (_covers.TryGetValue(key, out var cached)) return cached;
        string? cover = null;
        try
        {
            var url = "https://itunes.apple.com/search?entity=album&limit=10&term=" + Uri.EscapeDataString(track.Artist + " " + track.Album);
            using var response = await _http.GetAsync(url, _stop.Token);
            response.EnsureSuccessStatusCode();
            using var json = await System.Text.Json.JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(_stop.Token), cancellationToken: _stop.Token);
            foreach (var item in json.RootElement.GetProperty("results").EnumerateArray())
            {
                if (!MatchesAlbum(track, item.GetProperty("artistName").GetString(), item.GetProperty("collectionName").GetString())) continue;
                cover = PublicArtwork(item.GetProperty("artworkUrl100").GetString())?.Replace("100x100bb", "600x600bb");
                if (cover is not null) break;
            }
            _covers[key] = cover;
        }
        catch (Exception error) when (error is HttpRequestException or System.Text.Json.JsonException or OperationCanceledException)
        {
            PlaybackDiagnostics.Write("Discord", "Busca de capa indisponível; presença continua sem capa.");
            _covers[key] = null;
        }
        return cover;
    }

    public static bool MatchesAlbum(Track track, string? artist, string? album)
    {
        static string Normalize(string? value) => string.Concat((value ?? "").Normalize().EnumerateRunes()
            .Where(Rune.IsLetterOrDigit).Select(r => Rune.ToLowerInvariant(r).ToString()));
        return Normalize(track.Artist) == Normalize(artist) && Normalize(track.Album) == Normalize(album);
    }

    public static string? PublicArtwork(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && Uri.CheckHostName(uri.Host) == UriHostNameType.Dns
        && !uri.IsLoopback && string.IsNullOrEmpty(uri.UserInfo) ? url : null;

    public sealed record DiscordActivity
    {
        [JsonPropertyName("type")] public int Type { get; init; } = 2;
        [JsonPropertyName("name")] public string Name { get; init; } = "";
        [JsonPropertyName("details"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Details { get; init; }
        [JsonPropertyName("state"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? State { get; init; }
        [JsonPropertyName("timestamps")] public DiscordTimestamps? Timestamps { get; init; }
        [JsonPropertyName("assets")] public DiscordAssets? Assets { get; init; }
        [JsonPropertyName("buttons")] public DiscordButton[]? Buttons { get; init; }
    }
    public sealed record DiscordTimestamps(
        [property: JsonPropertyName("start")] long Start,
        [property: JsonPropertyName("end")] long? End);
    public sealed record DiscordAssets(
        [property: JsonPropertyName("large_image")] string LargeImage,
        [property: JsonPropertyName("large_text"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LargeText,
        [property: JsonPropertyName("small_image"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SmallImage,
        [property: JsonPropertyName("small_text"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SmallText);
    public sealed record DiscordButton(
        [property: JsonPropertyName("label")] string Label,
        [property: JsonPropertyName("url")] string Url);

    public static DiscordActivity Build(Track track, double position, double duration, string? cover, DateTime now)
    {
        position = double.IsFinite(position) ? Math.Clamp(position, 0, 31_536_000) : 0;
        duration = double.IsFinite(duration) ? Math.Clamp(duration, 0, 31_536_000) : 0;
        var info = DiscordActivityDisplay.Format(track);
        var art = PublicArtwork(cover);
        var youtube = !track.IsLocal && Uri.TryCreate(track.Source, UriKind.Absolute, out var source)
            && source.Scheme == "https" && source.Host is "www.youtube.com" or "youtube.com" or "youtu.be";
        var start = new DateTimeOffset(DateTime.SpecifyKind(now, DateTimeKind.Utc)).AddSeconds(-position).ToUnixTimeSeconds();
        var end = duration > position
            ? new DateTimeOffset(DateTime.SpecifyKind(now, DateTimeKind.Utc)).AddSeconds(duration - position).ToUnixTimeSeconds()
            : (long?)null;
        return new DiscordActivity
        {
            Name = info.Name, Details = info.Details, State = info.State,
            Timestamps = new DiscordTimestamps(start, end),
            Assets = new DiscordAssets(art ?? DiscordActivityDisplay.IconUrl,
                info.ArtworkText, art is null ? null : DiscordActivityDisplay.IconUrl,
                art is null ? null : DiscordActivityDisplay.AppName),
            Buttons = youtube ? [new DiscordButton("Ouvir no YouTube", track.Source)] : null
        };
    }

    public void Dispose()
    {
        _disposed = true; _music.Changed -= Update; _music.Tick -= Update;
        _stop.Cancel(); _client.Dispose(); _http.Dispose(); _stop.Dispose();
    }
}
