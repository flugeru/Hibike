using Avalonia;
using Hibike.App;
using Hibike.Core.Services;

namespace Hibike.Desktop;
internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
#if WINDOWS
        try { WindowsAppIdentity.Register(); }
        catch (Exception error) { PlaybackDiagnostics.Write("Identidade Windows", error.Message); }
#endif
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hibike");
        // Keep existing absolute audio/artwork paths valid for installations created before rename.
        var legacyDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hibiki");
        if (!Directory.Exists(directory) && Directory.Exists(legacyDirectory)) directory = legacyDirectory;
        AppServices.Configure(new YtDlpCatalog(new DesktopCommandRunner(), directory), DesktopAudio.Configure(directory), directory);
        AppServices.ReadMediaInfo = MediaInfoReader.ReadAsync;
#if WINDOWS
        AppServices.ConnectYouTubeAccount = WebAccountLogin.ConnectAsync;
        WebSessionStore.Configure();
#else
        AppServices.ConnectYouTubeAccount = () => GoogleAccountLogin.ConnectAsync(directory);
#endif
        DiscordPresence? presence = null;
        IDisposable? mediaSession = null;
        try
        {
            AppBuilder.Configure<Hibike.App.App>().UsePlatformDetect().WithInterFont()
                .AfterSetup(_ => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
#if WINDOWS
                    if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                        && desktop.MainWindow is { } window)
                    {
                        void ConnectMediaSession()
                        {
                            try { mediaSession = new WindowsMediaSession(window, AppServices.CreateController()); }
                            catch (Exception error) { PlaybackDiagnostics.Write("Mídia Windows", error.Message); }
                        }
                        if (window.TryGetPlatformHandle() is not null) ConnectMediaSession();
                        else window.Opened += (_, _) => ConnectMediaSession();
                    }
#endif
                    if (Environment.GetEnvironmentVariable("HIBIKE_DISCORD_RPC") != "0")
                    {
                        try { presence = new DiscordPresence(AppServices.CreateController()); }
                        catch (Exception error) { PlaybackDiagnostics.Write("Discord", error.Message); }
                    }
                })).StartWithClassicDesktopLifetime(args);
        }
        finally { mediaSession?.Dispose(); presence?.Dispose(); AppServices.Suspend(); AppServices.Player?.Dispose(); }
    }
}
