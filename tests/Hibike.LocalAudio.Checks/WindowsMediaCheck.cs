#if WINDOWS
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Hibike.App;
using Hibike.Core.Models;
using Hibike.Core.Services;
using Hibike.Desktop;
using Windows.Media.Control;

internal static class WindowsMediaCheck
{
    [STAThread]
    public static void Run(string path)
    {
        var directory = Path.Combine(Path.GetTempPath(), "hibike-media-" + Guid.NewGuid().ToString("N"));
        var player = new VlcAudioPlayer { Volume = 0 };
        AppServices.Configure(new YtDlpCatalog(new DesktopCommandRunner()), player, directory);
        var result = 1;
        AppBuilder.Configure<Hibike.App.App>().UsePlatformDetect().WithInterFont()
            .AfterSetup(_ => Dispatcher.UIThread.Post(async () =>
            {
                var lifetime = (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
                IDisposable? integration = null;
                try
                {
                    await Task.Delay(400);
                    var controller = AppServices.CreateController();
                    integration = (IDisposable)Activator.CreateInstance(typeof(VlcAudioPlayer).Assembly.GetType("Hibike.Desktop.WindowsMediaSession")!, lifetime.MainWindow!, controller)!;
                    await controller.PlayAsync(new Track("local", "filename", "local", path, IsLocal: true));
                    var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                    GlobalSystemMediaTransportControlsSession? session = null;
                    for (var i = 0; i < 20 && session is null; i++)
                    {
                        await Task.Delay(500);
                        foreach (var candidate in manager.GetSessions())
                            if ((await candidate.TryGetMediaPropertiesAsync()).Title == controller.CurrentSource?.Title) { session = candidate; break; }
                    }
                    if (session is null) throw new Exception("Hibike missing from Windows media sessions.");
                    var metadata = await session.TryGetMediaPropertiesAsync();
                    if (metadata.Artist != controller.CurrentSource?.Artist || metadata.Thumbnail is null) throw new Exception("Windows metadata or artwork missing.");
                    using var thumbnail = await metadata.Thumbnail.OpenReadAsync();
                    if (thumbnail.Size == 0) throw new Exception("Empty Windows artwork.");
                    if (!await session.TryPauseAsync()) throw new Exception("Windows rejected pause.");
                    await Task.Delay(800);
                    if (player.IsPlaying) throw new Exception("Windows pause did not reach player.");
                    if (!await session.TryPlayAsync()) throw new Exception("Windows rejected play.");
                    await Task.Delay(800);
                    if (!player.IsPlaying) throw new Exception("Windows play did not reach player.");
                    Console.WriteLine("PASS: Windows session, title, artist, artwork, pause and play."); result = 0;
                }
                catch (Exception error) { Console.Error.WriteLine(error); }
                finally { integration?.Dispose(); player.Pause(); lifetime.Shutdown(result); }
            })).StartWithClassicDesktopLifetime([]);
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
        if (result != 0) throw new Exception("Windows media check failed.");
    }
}
#endif
