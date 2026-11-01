#if HIBIKI_SMOKE_TEST
using System.Text;
using Android.Content;
using Hibiki.App;
using Hibiki.Core.Models;
using Hibiki.Core.Services;

namespace Hibiki.Android;
internal static class PlaybackProbe
{
    public static async Task RunAsync(Context context)
    {
        await Task.Delay(1500);
        var player = AppServices.Player!;
        try
        {
            var path = Path.Combine(context.CacheDir!.AbsolutePath, "probe.wav");
            using (var stream = File.Create(path))
            using (var writer = new BinaryWriter(stream))
            {
                const int rate = 22050, samples = rate * 90;
                writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples * 2);
                writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
                writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2);
                writer.Write((short)2); writer.Write((short)16); writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(samples * 2);
                for (var i = 0; i < samples; i++) writer.Write((short)(Math.Sin(i * 2 * Math.PI * 440 / rate) * 1000));
            }
            using var localTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            var watch = System.Diagnostics.Stopwatch.StartNew();
            await AppServices.Controller!.PlayAsync(new("local-probe", "Streaming test", "Hibiki CI", path, 90, IsLocal: true));
            await Task.Delay(1200);
            if (!player.IsPlaying || player.Position < 0.3) throw new IOException("Native audio position did not advance.");
            global::Android.Util.Log.Info("HibikiProbe", "HIBIKI_PROBE_LOCAL_OK startup_ms=" + watch.ElapsedMilliseconds + " position=" + player.Position);
            var before = player.Position;
            context.StartActivity(new Intent(Intent.ActionMain).AddCategory(Intent.CategoryHome)!.AddFlags(ActivityFlags.NewTask));
            await Task.Delay(1600);
            if (!player.IsPlaying || player.Position <= before + 0.5) throw new IOException("Playback stopped in background.");
            global::Android.Util.Log.Info("HibikiProbe", "HIBIKI_PROBE_BACKGROUND_OK position=" + player.Position);
            var manager = (global::Android.App.NotificationManager)context.GetSystemService(Context.NotificationService)!;
            var notifications = manager.GetActiveNotifications();
            if (notifications is null || !notifications.Any(n => n.Id == 42)) throw new IOException("Media notification missing.");
            global::Android.Util.Log.Info("HibikiProbe", "HIBIKI_PROBE_NOTIFICATION_OK");
            context.StartActivity(context.PackageManager!.GetLaunchIntentForPackage(context.PackageName!)!.AddFlags(ActivityFlags.NewTask));
            await Task.Delay(400);
            player.Pause();
            // Layout fixture exists only in this CI-only emulator build.
            var controller = AppServices.Controller!;
            controller.Results.Clear();
            controller.Results.Add(new("jNQXAC9IVRw", "Hikari — Clovis Reyes & VDYCD", "VDYCD and Clovis Reyes", "https://www.youtube.com/watch?v=jNQXAC9IVRw", 150));
            controller.Results.Add(new("fixture-2", "BellyJay — MONTAGEM HIKARI", "TheGoodVibe", "https://www.youtube.com/", 90));
            controller.Results.Add(new("fixture-3", "Hikari (Slowed + Reverb)", "Clovis Reyes", "https://www.youtube.com/", 168));
            controller.SetStatus("CI layout fixture");
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error("HibikiProbe", "HIBIKI_PROBE_LOCAL_FAILED " + ex);
            return;
        }
        var runner = new AndroidCommandRunner(context);
        try
        {
            using var runtimeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var version = await runner.RunAsync(["--version"], runtimeTimeout.Token);
            if (version.ExitCode != 0) throw new IOException(version.Error);
            global::Android.Util.Log.Info("HibikiProbe", "HIBIKI_PROBE_RUNTIME_OK " + version.Output.Trim());
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error("HibikiProbe", "HIBIKI_PROBE_RUNTIME_FAILED " + ex.Message);
            return;
        }
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
            var catalog = new YtDlpCatalog(runner, context.FilesDir!.AbsolutePath);
            var source = await catalog.ResolveAsync(new("jNQXAC9IVRw", "YT-DLP public test video", "YT-DLP", "https://www.youtube.com/watch?v=jNQXAC9IVRw"), timeout.Token);
            await player.PlayAsync(source, timeout.Token);
            await Task.Delay(1200);
            if (!player.IsPlaying || player.Position < 0.3) throw new IOException("Remote audio position did not advance.");
            global::Android.Util.Log.Info("HibikiProbe", "HIBIKI_PROBE_REMOTE_OK position=" + player.Position);
            player.Pause();
        }
        catch (Exception ex)
        {
            PlaybackDiagnostics.Write("Probe", ex.GetType().Name + ": " + ex.Message);
            global::Android.Util.Log.Warn("HibikiProbe", "HIBIKI_PROBE_REMOTE_FAILED (see diagnostics; YouTube may restrict CI egress)");
        }
        finally
        {
            foreach (var line in PlaybackDiagnostics.Snapshot().Split('\n')) global::Android.Util.Log.Info("HibikiProbe", line);
            global::Android.Util.Log.Info("HibikiProbe", "HIBIKI_PROBE_DONE");
            Avalonia.Threading.Dispatcher.UIThread.Post(() => AppServices.OpenPlayerView?.Invoke());
        }
    }
}
#endif
