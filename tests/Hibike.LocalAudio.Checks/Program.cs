using System.Text.Json;
using Hibike.Core.Models;
using Hibike.Desktop;
using Hibike.Core.Services;

#if WINDOWS
if (args is ["--windows-media", var mediaPath])
{
    WindowsMediaCheck.Run(mediaPath); return;
}
#endif
var now = DateTime.UtcNow;
var presenceTrack = new Track("test", new string('音', 100), "Artist", "https://www.youtube.com/watch?v=test", Album: "Album");
var presence = DiscordPresence.Build(presenceTrack, 30, 180, "https://example.org/cover.jpg", now);
var start = new DateTimeOffset(DateTime.SpecifyKind(now, DateTimeKind.Utc)).ToUnixTimeSeconds();
if (presence.Type != 2 || presence.Name != new string('音', 42)
    || presence.Timestamps?.Start != start - 30 || presence.Timestamps.End != start + 150
    || presence.Details != "Artist" || presence.State != "Album · Hibike"
    || presence.Assets?.LargeImage != "https://example.org/cover.jpg"
    || presence.Assets?.SmallImage != Hibike.App.DiscordActivityDisplay.IconUrl
    || presence.Assets.SmallText != "Hibike") throw new Exception("Invalid Discord listening activity.");
var titlePresence = DiscordPresence.Build(presenceTrack with { Title = "Une vie à t'aimer" }, 30, 180, "https://example.org/cover.jpg", now);
if (titlePresence.Name != "Une vie à t'aimer" || titlePresence.Details != "Artist" || titlePresence.State != "Album · Hibike")
    throw new Exception("Wrong title / artist / album layout.");
var localPresence = DiscordPresence.Build(presenceTrack with { IsLocal = true, Source = "C:\\private.flac" }, 0, 0, "file:///C:/cover.jpg", now);
if (localPresence.Buttons is not null
    || localPresence.Assets?.LargeImage != Hibike.App.DiscordActivityDisplay.IconUrl
    || localPresence.Assets?.SmallImage is not null) throw new Exception("Local paths exposed in presence.");
if (!DiscordPresence.MatchesAlbum(presenceTrack, "ARTIST", "Album!") || DiscordPresence.MatchesAlbum(presenceTrack, "Other", "Album"))
    throw new Exception("Album matching failed.");
var payload = JsonSerializer.SerializeToElement(titlePresence);
if (payload.GetProperty("name").GetString() != "Une vie à t'aimer"
    || payload.GetProperty("details").GetString() != "Artist"
    || payload.GetProperty("state").GetString() != "Album · Hibike"
    || payload.GetProperty("assets").GetProperty("small_image").GetString() != Hibike.App.DiscordActivityDisplay.IconUrl
    || payload.GetProperty("type").GetInt32() != 2)
    throw new Exception("Discord IPC JSON payload invalid.");
var missingAlbum = DiscordPresence.Build(presenceTrack with { Album = null }, 0, 180, null, now);
var missingArtist = DiscordPresence.Build(presenceTrack with { Artist = "" }, 0, 180, null, now);
var missingAlbumJson = JsonSerializer.SerializeToElement(missingAlbum);
var missingArtistJson = JsonSerializer.SerializeToElement(missingArtist);
if (missingAlbumJson.GetProperty("state").GetString() != "Hibike" || missingArtistJson.TryGetProperty("details", out _)
    || missingAlbumJson.GetProperty("assets").TryGetProperty("large_text", out _))
    throw new Exception("Unknown album/artist text must be omitted.");
var duplicate = DiscordPresence.Build(presenceTrack with { Album = " artist " }, 0, 180, null, now);
if (duplicate.Details != "Artist" || duplicate.State != "Hibike" || duplicate.Assets?.LargeText is not null)
    throw new Exception("Artist repeated as album in desktop presence.");

var buildAccountActivity = typeof(Hibike.App.AppServices).Assembly.GetType("Hibike.App.DiscordAccountPresence")!
    .GetMethod("BuildActivity", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
var accountTrack = presenceTrack with { Title = "Faixa", Artist = "MAFUYU", Album = " mafuyu " };
var accountPayload = JsonSerializer.SerializeToElement(buildAccountActivity.Invoke(null,
    [accountTrack, 0d, 180d, "mp:cover", DateTimeOffset.UtcNow, "mp:icon"]));
if (accountPayload.GetProperty("name").GetString() != "Faixa"
    || accountPayload.GetProperty("details").GetString() != "MAFUYU"
    || accountPayload.GetProperty("state").GetString() != "Hibike"
    || accountPayload.GetProperty("assets").GetProperty("small_image").GetString() != "mp:icon")
    throw new Exception("Account presence must show the title, artist and Hibike without repeating the album.");

var longAlbum = DiscordPresence.Build(presenceTrack with { Album = new string('音', 100) }, 0, 180, null, now);
if (!longAlbum.State!.EndsWith(" · Hibike", StringComparison.Ordinal)
    || System.Text.Encoding.UTF8.GetByteCount(longAlbum.State) > 128)
    throw new Exception("Long album displaced the application label.");
if (args is ["--presence-only"]) { Console.WriteLine("Discord payload checks passed."); return; }
if (args.Length != 1 || !File.Exists(args[0])) throw new ArgumentException("Pass existing local audio file.");
using var player = new VlcAudioPlayer();
player.Volume = 0;
var directory = Path.Combine(Path.GetTempPath(), "hibike-local-check-" + Guid.NewGuid().ToString("N"));
var track = LocalTrackMetadata.Read(new Track("local", "filename", "Áudio local", args[0], IsLocal: true), directory);
if (track.Title == "filename" || track.Artist == "Áudio local" || track.DurationSeconds <= 0 || track.Thumbnail is null || !File.Exists(new Uri(track.Thumbnail).LocalPath))
    throw new Exception("Missing embedded FLAC tags or artwork.");
await player.PlayAsync(new AudioSource(args[0], new Dictionary<string, string>(), track.Title, track.Artist, track.Thumbnail), default);
var source = player.PlaybackSource!;
if (source.SampleRateHz is not > 0 || source.Channels is not > 0 || string.IsNullOrEmpty(source.Codec))
    throw new Exception("Missing local audio metadata.");
if (source.Container == "flac" && (source.BitrateKbps is not > 0 || source.BitDepth is not > 0))
    throw new Exception("Missing FLAC stream metadata.");
Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(source));
await Task.Delay(500);
player.Pause();
Directory.Delete(directory, true);
