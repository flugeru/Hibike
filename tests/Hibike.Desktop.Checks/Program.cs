using System.Reflection;
using Hibike.Desktop;
using Hibike.Core.Models;
using Hibike.Core.Services;

// A preferência deve preservar o nome original por padrão e alterar apenas a primeira linha.
var discordTrack = new Track("discord-check", "Faixa de teste", "Artista", "https://www.youtube.com/watch?v=test");
var standardPresence = DiscordPresence.Build(discordTrack, 0, 180, null, DateTime.UtcNow);
var titlePresence = DiscordPresence.Build(discordTrack, 0, 180, null, DateTime.UtcNow, titleAsName: true);
if (standardPresence.Name != "Hibike" || titlePresence.Name != "Faixa de teste"
    || titlePresence.Details != standardPresence.Details || titlePresence.State != standardPresence.State)
    throw new Exception("Desktop Discord activity-name option failed.");
var accountPresenceType = typeof(Hibike.App.AppServices).Assembly.GetType("Hibike.App.DiscordAccountPresence")!;
var buildAndroidActivity = accountPresenceType.GetMethod("BuildActivity", BindingFlags.NonPublic | BindingFlags.Static)!;
string GetAndroidActivityName(bool titleAsName)
{
    var payload = buildAndroidActivity.Invoke(null, [discordTrack, 0d, 180d, null, DateTimeOffset.UtcNow, titleAsName]);
    return System.Text.Json.JsonSerializer.SerializeToElement(payload).GetProperty("name").GetString()!;
}
if (GetAndroidActivityName(false) != "Hibike" || GetAndroidActivityName(true) != "Faixa de teste")
    throw new Exception("Android Discord activity-name option failed.");
var personal = new Hibike.App.MusicController.Personalization { DiscordUseTrackTitleAsName = true };
if (System.Text.Json.JsonSerializer.Deserialize<Hibike.App.MusicController.Personalization>(
        System.Text.Json.JsonSerializer.Serialize(personal))?.DiscordUseTrackTitleAsName != true)
    throw new Exception("Discord activity-name preference persistence failed.");
Console.WriteLine("Discord activity-name checks passed.");

var login = typeof(VlcAudioPlayer).Assembly.GetType("Hibike.Desktop.WebAccountLogin")!;
var mediaInfo = typeof(VlcAudioPlayer).Assembly.GetType("Hibike.Desktop.MediaInfoReader")!;
var mediaDetails = (IReadOnlyDictionary<string, string>)mediaInfo.GetMethod("Parse", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, ["""{"media":{"track":[{"@type":"General"},{"@type":"Audio","Format":"FLAC","SamplingRate":"96000","BitDepth":"24"}]}}"""])!;
if (mediaDetails["Codec · MediaInfo"] != "FLAC" || mediaDetails["Amostragem · MediaInfo"] != "96000 Hz" || mediaDetails["Profundidade · MediaInfo"] != "24 bits") throw new Exception("MediaInfo parsing failed.");
var allowed = login.GetMethod("Allowed", BindingFlags.Static | BindingFlags.NonPublic)!;
foreach (var (url, expected) in new[] { ("https://accounts.google.com/signin", true), ("https://accounts.google.co.uk/signin", true), ("https://accounts.google.co.uk.evil.example/", false), ("https://evilaccounts.google.co.uk/", false), ("http://accounts.google.co.uk/", false), ("https://www.youtube.com/signin", true), ("https://music.youtube.com/", true), ("https://google.com.evil.example/", false), ("https://evilgoogle.com/", false), ("http://accounts.google.com/", false), ("file:///private", false) })
    if ((bool)allowed.Invoke(null, [url])! != expected) throw new Exception("Login navigation validation failed.");
Console.WriteLine("Login navigation checks passed.");
var sessionDirectory = Path.Combine(Path.GetTempPath(), "hibike-session-check-" + Guid.NewGuid());
try
{
    typeof(VlcAudioPlayer).Assembly.GetType("Hibike.Desktop.WebSessionStore")!.GetMethod("Configure", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [sessionDirectory]);
    using var client = new HttpClient();
    using var account = YouTubeAccountLibrary.FromWebSession(client, "SAPISID=test-session-check");
    Hibike.App.AppServices.SaveYouTubeAccount!(account);
    var path = Path.Combine(sessionDirectory, "google-session.bin");
    if (System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains("test-session-check")) throw new Exception("Saved session is plaintext.");
    // Recreate platform hooks, as startup does, before reading persisted bytes.
    Hibike.App.AppServices.RestoreYouTubeAccount = null;
    typeof(VlcAudioPlayer).Assembly.GetType("Hibike.Desktop.WebSessionStore")!.GetMethod("Configure", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [sessionDirectory]);
    using var restored = await Hibike.App.AppServices.RestoreYouTubeAccount!();
    if (restored?.ExportWebSession() != account.ExportWebSession()) throw new Exception("Session restore failed.");
    Hibike.App.AppServices.ClearYouTubeAccount!();
    if (await Hibike.App.AppServices.RestoreYouTubeAccount!() is not null) throw new Exception("Logout did not clear session.");
    Console.WriteLine("Protected session encryption, restore and logout passed.");
}
finally { if (Directory.Exists(sessionDirectory)) Directory.Delete(sessionDirectory); }
if (args.Contains("--engines"))
{
    var path = Path.Combine(Path.GetTempPath(), "hibike-engines-" + Guid.NewGuid() + ".wav");
    try
    {
        using (var writer = new BinaryWriter(File.Create(path)))
        {
            writer.Write("RIFF"u8); writer.Write(36 + 88200 * 4); writer.Write("WAVEfmt "u8);
            writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(44100);
            writer.Write(88200); writer.Write((short)2); writer.Write((short)16);
            writer.Write("data"u8); writer.Write(88200 * 4); writer.Write(new byte[88200 * 4]);
        }
        foreach (var name in new[] { "libmpv", "VLC" })
        {
            using IAudioPlayer engine = name == "libmpv" ? new MpvAudioPlayer() : new VlcAudioPlayer();
            var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            engine.Ended += (_, _) => ended.TrySetResult();
            engine.Failed += (_, error) => ended.TrySetException(new IOException(error));
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            try { await engine.PlayAsync(new AudioSource(path, new Dictionary<string, string>()), canceled.Token); throw new Exception("Cancellation ignored."); } catch (OperationCanceledException) { }
            await engine.PlayAsync(new AudioSource(path, name == "libmpv" ? new Dictionary<string, string> { ["Accept"] = "audio/*", ["Accept-Language"] = "pt-BR,en;q=0.9" } : new Dictionary<string, string>()), default).WaitAsync(TimeSpan.FromSeconds(15));
            var until = DateTime.UtcNow.AddSeconds(5);
            while (engine.Position <= .1 && DateTime.UtcNow < until) await Task.Delay(50);
            if (!engine.IsPlaying || engine.Position <= 0 || engine.Duration < 3) throw new Exception(name + ": playback did not advance.");
            engine.Pause(); await Task.Delay(300);
            if (engine.IsPlaying) throw new Exception(name + ": pause failed.");
            engine.Volume = .25; if (Math.Abs(engine.Volume - .25) > .001) throw new Exception("Volume failed.");
            engine.Seek(2); engine.Resume();
            await ended.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Console.WriteLine(name + ": playback, duration, pause, resume, seek, volume, EOF and cancellation passed.");
        }
    }
    finally
    {
        // VLC disposes on its command queue; allow that queue to release the file.
        for (var attempt = 0; ; attempt++)
        {
            try { File.Delete(path); break; }
            catch (IOException) when (attempt < 50) { await Task.Delay(100); }
        }
    }
}
if (args.Contains("--online-mpv"))
{
    using var engine = new MpvAudioPlayer();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(70));
    var catalog = new CachedMusicCatalog(new YtDlpCatalog(new DesktopCommandRunner()));
    var tracks = await catalog.SearchAsync("Shayne", timeout.Token);
    var source = await catalog.ResolveAsync(tracks.First(t => !t.IsArtist), timeout.Token);
    await engine.PlayAsync(source, timeout.Token);
    while (engine.Position <= .2) await Task.Delay(100, timeout.Token);
    if (!engine.IsPlaying) throw new Exception("Online libmpv did not start.");
    Console.WriteLine("Online libmpv playback advanced with yt-dlp headers.");
}
using var player = new VlcAudioPlayer();
var queue = typeof(VlcAudioPlayer).GetMethod("QueueCommand", BindingFlags.Instance | BindingFlags.NonPublic)!;
Task Enqueue(Func<Task> action) => (Task)queue.Invoke(player, [action])!;
using var release = new ManualResetEventSlim();
var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var first = Enqueue(() => { entered.SetResult(); release.Wait(); return Task.CompletedTask; });
try
{
    await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    var ran = false;
    var second = Enqueue(() => { ran = true; return Task.CompletedTask; });
    if (ran || second.IsCompleted) throw new Exception("Native commands overlapped.");
    using (var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(100)))
    {
        try
        {
            await player.PlayAsync(new AudioSource("unused", new Dictionary<string, string>()), timeout.Token).WaitAsync(TimeSpan.FromSeconds(2));
            throw new Exception("Blocked playback ignored cancellation.");
        }
        catch (OperationCanceledException) { }
    }
    _ = player.Position; _ = player.Duration; _ = player.IsPlaying;
    release.Set();
    await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
    if (!ran) throw new Exception("Queued command missing.");
    try { await Enqueue(() => throw new IOException("Expected failure")); } catch (IOException) { }
    await Enqueue(() => Task.CompletedTask).WaitAsync(TimeSpan.FromSeconds(5));
    if (args.Contains("--native"))
    {
        var path = Path.Combine(Path.GetTempPath(), "hibike-playback-" + Guid.NewGuid() + ".wav");
        try
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                writer.Write("RIFF"u8); writer.Write(36 + 44100 * 2 * 4); writer.Write("WAVEfmt "u8);
                writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(44100);
                writer.Write(88200); writer.Write((short)2); writer.Write((short)16);
                writer.Write("data"u8); writer.Write(44100 * 2 * 4); writer.Write(new byte[44100 * 2 * 4]);
            }
            for (var i = 0; i < 3; i++)
            {
                player.Pause();
                await player.PlayAsync(new AudioSource(path, new Dictionary<string, string>()), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(15));
                var until = DateTime.UtcNow.AddSeconds(5);
                while (player.Position == 0 && DateTime.UtcNow < until) await Task.Delay(50);
                if (!player.IsPlaying || player.Position <= 0) throw new Exception("Native playback did not advance.");
            }
            Console.WriteLine("Native VLC checks passed: three local starts and advancing playback.");
        }
        finally
        {
            await Enqueue(() =>
            {
                var native = typeof(VlcAudioPlayer).GetField("_player", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(player);
                native?.GetType().GetMethod("Stop", Type.EmptyTypes)!.Invoke(native, null);
                return Task.CompletedTask;
            });
            Console.WriteLine(PlaybackDiagnostics.Snapshot()); File.Delete(path);
        }
    }
    if (args.Contains("--online"))
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(55));
        var catalog = new CachedMusicCatalog(new YtDlpCatalog(new DesktopCommandRunner()));
        var tracks = await catalog.SearchAsync("Shayne", timeout.Token);
        var track = tracks.First(t => !t.IsArtist);
        Console.WriteLine("Resolving: " + track.Title);
        try
        {
            var source = await catalog.ResolveAsync(track, timeout.Token);
            player.Pause();
            await player.PlayAsync(source, timeout.Token);
            while (player.Position <= 0) await Task.Delay(100, timeout.Token);
            if (!player.IsPlaying) throw new Exception("Online playback did not start.");
            Console.WriteLine("Online VLC playback advanced.");
        }
        finally { Console.WriteLine(PlaybackDiagnostics.Snapshot()); }
    }
    Console.WriteLine("Desktop checks passed: nonblocking state, command serialization, cancellable loading, recovery after failure.");
}
finally { release.Set(); }
