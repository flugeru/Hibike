#if HIBIKI_SMOKE_TEST
using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.VisualTree;
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
            var startupMs = watch.ElapsedMilliseconds;
            await Task.Delay(1200);
            if (!player.IsPlaying || player.Position < 0.3) throw new IOException("Native audio position did not advance.");
            global::Android.Util.Log.Info("HibikiProbe", "HIBIKI_PROBE_LOCAL_OK startup_ms=" + startupMs + " position=" + player.Position);
            global::Android.Util.Log.Info("HibikiProbe", $"HIBIKI_PROBE_BUFFER duration={player.Duration:F1}s buffered={PlaybackService.Instance!.BufferedPosition:F1}s position={player.Position:F1}s");
            using var nativeController = new global::Android.Media.Session.MediaController(context, PlaybackService.Instance!.PlatformToken!);
            void Key(global::Android.Views.Keycode key)
            {
                nativeController.DispatchMediaButtonEvent(new global::Android.Views.KeyEvent(global::Android.Views.KeyEventActions.Down, key));
                nativeController.DispatchMediaButtonEvent(new global::Android.Views.KeyEvent(global::Android.Views.KeyEventActions.Up, key));
            }
            Key(global::Android.Views.Keycode.MediaPause); await Task.Delay(500);
            if (player.IsPlaying) throw new IOException("Media session pause did not reach the player.");
            Key(global::Android.Views.Keycode.MediaPlay); await Task.Delay(500);
            if (!player.IsPlaying) throw new IOException("Media session play did not reach the player.");
            var queueController = AppServices.Controller!;
            queueController.Enqueue(new("probe-next", "Next track test", "Hibiki CI", path, 90, IsLocal: true));
            Key(global::Android.Views.Keycode.MediaNext);
            for (var i = 0; i < 30 && queueController.Current?.Id != "probe-next"; i++) await Task.Delay(100);
            if (queueController.Current?.Id != "probe-next") throw new IOException("Media session next did not advance the queue.");
            Key(global::Android.Views.Keycode.MediaPrevious);
            for (var i = 0; i < 30 && queueController.Current?.Id != "local-probe"; i++) await Task.Delay(100);
            if (queueController.Current?.Id != "local-probe") throw new IOException("Media session previous did not return to the queue entry.");
            global::Android.Util.Log.Info("HibikiProbe", "HIBIKI_PROBE_MEDIA_CONTROLS_OK play/pause/next/previous");
            var before = player.Position;
            context.StartActivity(new Intent(Intent.ActionMain).AddCategory(Intent.CategoryHome)!.AddFlags(ActivityFlags.NewTask));
            await Task.Delay(1600);
            if (!player.IsPlaying || player.Position <= before + 0.5) throw new IOException("Playback stopped in background.");
            global::Android.Util.Log.Info("HibikiProbe", "HIBIKI_PROBE_BACKGROUND_OK position=" + player.Position);
            var manager = (global::Android.App.NotificationManager)context.GetSystemService(Context.NotificationService)!;
            var notifications = manager.GetActiveNotifications();
            if (notifications is null || !notifications.Any(n => n.Id == 42)) throw new IOException("Media notification missing.");
            global::Android.Util.Log.Info("HibikiProbe", "HIBIKI_PROBE_NOTIFICATION_OK");
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
            try
            {
                context.StartActivity(new Intent(context, typeof(MainActivity)).AddFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop));
                await Task.Delay(500);
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => AppServices.OpenPlayerView?.Invoke());
                await Task.Delay(300);
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { AppServices.BackHandler?.Invoke(); });
                await Task.Delay(300);
                var layoutOk = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var view = (Avalonia.Application.Current?.ApplicationLifetime as ISingleViewApplicationLifetime)?.MainView;
                    if (view is null) return false;
                    var title = view.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Text == "Hikari — Clovis Reyes & VDYCD" && t.IsEffectivelyVisible);
                    var navigation = view.GetVisualDescendants().OfType<Button>().Where(b => b.Tag is ValueTuple<string, string, string, bool> tag && tag.Item4).ToArray();
                    global::Android.Util.Log.Info("HibikiProbe", "LAYOUT_BOUNDS title=" + title?.Bounds.ToString() + " nav=" + string.Join(",", navigation.Select(b => b.Bounds.Width)) + " view=" + view.Bounds.Width);
                    return title is not null && title.Bounds.Width > 40 && title.Bounds.Height > 5 && navigation.Length == 3
                        && navigation.All(b => b.Bounds.Width >= view.Bounds.Width / 3 - 2);
                });
                global::Android.Util.Log.Info("HibikiProbe", layoutOk ? "HIBIKI_PROBE_LAYOUT_OK titles/navigation/return" : "HIBIKI_PROBE_LAYOUT_FAILED titles/navigation/return");
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => AppServices.OpenPlayerView?.Invoke());
            }
            catch (Exception ex) { global::Android.Util.Log.Error("HibikiProbe", "HIBIKI_PROBE_LAYOUT_FAILED " + ex.Message); }
            global::Android.Util.Log.Info("HibikiProbe", "HIBIKI_PROBE_DONE");
        }
    }
}
#endif
