using System.Text;
using System.Text.Json;
using DiscordRPC;
using Hibiki.App;
using Hibiki.Core.Models;
using Hibiki.Core.Services;

namespace Hibiki.Desktop;

public sealed class DiscordPresence : IDisposable
{
    public const string ApplicationId = "1566415890596241459";
    private readonly MusicController _music;
    private readonly DiscordRpcClient _client = new(ApplicationId);
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private readonly CancellationTokenSource _stop = new();
    private readonly Dictionary<string, string?> _covers = new();
    private bool _busy, _disposed, _visible;
    private string? _lastKey;
    private double _lastPosition;
    private DateTime _lastUpdate;

    public DiscordPresence(MusicController music)
    {
        _music = music;
        _client.OnReady += (_, _) => PlaybackDiagnostics.Write("Discord", "Rich Presence conectado.");
        _client.OnError += (_, error) => PlaybackDiagnostics.Write("Discord", error.Message);
        _client.Initialize();
        music.Changed += Update; music.Tick += Update;
        Update();
    }

    private async void Update()
    {
        if (_busy || _disposed) return;
        _busy = true;
        try
        {
            var track = _music.Current;
            if (track is null || !_music.Player.IsPlaying || _music.LoadingAudio)
            {
                if (_visible) _client.ClearPresence();
                _visible = false; _lastKey = null; return;
            }
            var position = _music.Player.Position;
            var now = DateTime.UtcNow;
            var key = track.Source + "\n" + track.Title + "\n" + track.Artist;
            if (_visible && key == _lastKey && Math.Abs(position - _lastPosition - (now - _lastUpdate).TotalSeconds) < 3) return;
            var cover = PublicArtwork(_music.CurrentSource?.Artwork ?? track.Thumbnail);
            if (cover is null && track.IsLocal) cover = await FindCoverAsync(track);
            if (_disposed || _music.Current != track || !_music.Player.IsPlaying || _music.LoadingAudio) return;
            position = _music.Player.Position; now = DateTime.UtcNow;
            var duration = _music.Player.Duration > 0 ? _music.Player.Duration : track.DurationSeconds;
            _client.SetPresence(Build(track, position, duration, cover, now));
            _visible = true; _lastKey = key; _lastPosition = position; _lastUpdate = now;
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { PlaybackDiagnostics.Write("Discord", error.Message); }
        finally { _busy = false; }
    }

    // ponytail: iTunes album art only; add another provider if album coverage proves insufficient.
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
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(_stop.Token), cancellationToken: _stop.Token);
            foreach (var item in json.RootElement.GetProperty("results").EnumerateArray())
            {
                if (!MatchesAlbum(track, item.GetProperty("artistName").GetString(), item.GetProperty("collectionName").GetString())) continue;
                cover = PublicArtwork(item.GetProperty("artworkUrl100").GetString())?.Replace("100x100bb", "600x600bb");
                if (cover is not null) break;
            }
            _covers[key] = cover;
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or OperationCanceledException)
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

    public static RichPresence Build(Track track, double position, double duration, string? cover, DateTime now)
    {
        static string Limit(string value)
        {
            var text = new StringBuilder(); var bytes = 0;
            foreach (var rune in value.EnumerateRunes())
            {
                if (bytes + rune.Utf8SequenceLength > 128) break;
                text.Append(rune); bytes += rune.Utf8SequenceLength;
            }
            return text.Length == 0 ? "—" : text.ToString();
        }
        position = double.IsFinite(position) ? Math.Max(0, position) : 0;
        duration = double.IsFinite(duration) ? Math.Max(0, duration) : 0;
        var title = track.IsLocal ? track.Title : TrackTextFormatter.Title(track.Title, track.Artist);
        return new RichPresence
        {
            Type = ActivityType.Listening,
            Details = Limit(title), State = Limit(track.IsLocal ? track.Artist : TrackTextFormatter.Artist(track.Artist)),
            Timestamps = new Timestamps { Start = now.AddSeconds(-position), End = duration > position ? now.AddSeconds(duration - position) : null },
            Assets = PublicArtwork(cover) is { } image ? new Assets { LargeImageKey = image, LargeImageText = Limit(track.Album ?? title) } : null,
            Buttons = !track.IsLocal && Uri.TryCreate(track.Source, UriKind.Absolute, out var source) && source.Scheme == "https"
                && (source.Host == "www.youtube.com" || source.Host == "youtube.com" || source.Host == "youtu.be")
                ? [new DiscordRPC.Button { Label = "Ouvir no YouTube", Url = track.Source }] : null
        };
    }

    public void Dispose()
    {
        _disposed = true; _music.Changed -= Update; _music.Tick -= Update;
        _stop.Cancel(); _client.Dispose(); _http.Dispose();
    }
}
