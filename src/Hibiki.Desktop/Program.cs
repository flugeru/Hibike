using Avalonia;
using Hibiki.App;
using Hibiki.Core.Services;

namespace Hibiki.Desktop;
internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hibiki");
        AppServices.Configure(new YtDlpCatalog(new DesktopCommandRunner(), directory), new VlcAudioPlayer(), directory);
        DiscordPresence? presence = null;
        try
        {
            AppBuilder.Configure<Hibiki.App.App>().UsePlatformDetect().WithInterFont()
                .AfterSetup(_ => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (Environment.GetEnvironmentVariable("HIBIKI_DISCORD_RPC") != "0")
                    {
                        try { presence = new DiscordPresence(AppServices.CreateController()); }
                        catch (Exception error) { PlaybackDiagnostics.Write("Discord", error.Message); }
                    }
                })).StartWithClassicDesktopLifetime(args);
        }
        finally { presence?.Dispose(); }
    }
}
