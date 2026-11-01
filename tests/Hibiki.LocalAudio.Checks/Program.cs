using Hibiki.Core.Models;
using Hibiki.Desktop;
using Hibiki.Core.Services;

var now = DateTime.UtcNow;
var presenceTrack = new Track("test", new string('音', 100), "Artist", "https://www.youtube.com/watch?v=test", Album: "Album");
var presence = DiscordPresence.Build(presenceTrack, 30, 180, "https://example.org/cover.jpg", now);
if (presence.Type != DiscordRPC.ActivityType.Listening || presence.Timestamps.Start != now.AddSeconds(-30)
    || presence.Timestamps.End != now.AddSeconds(150) || System.Text.Encoding.UTF8.GetByteCount(presence.Details) > 128
    || presence.Assets.LargeImageKey != "https://example.org/cover.jpg") throw new Exception("Invalid Discord listening activity.");
var localPresence = DiscordPresence.Build(presenceTrack with { IsLocal = true, Source = "C:\\private.flac" }, 0, 0, "file:///C:/cover.jpg", now);
if (localPresence.Buttons is not null || localPresence.Assets is not null) throw new Exception("Local paths exposed in presence.");
if (!DiscordPresence.MatchesAlbum(presenceTrack, "ARTIST", "Album!") || DiscordPresence.MatchesAlbum(presenceTrack, "Other", "Album"))
    throw new Exception("Album matching failed.");
if (args is ["--presence-only"]) { Console.WriteLine("Discord payload checks passed."); return; }
if (args.Length != 1 || !File.Exists(args[0])) throw new ArgumentException("Pass existing local audio file.");
using var player = new VlcAudioPlayer();
player.Volume = 0;
var directory = Path.Combine(Path.GetTempPath(), "hibiki-local-check-" + Guid.NewGuid().ToString("N"));
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
