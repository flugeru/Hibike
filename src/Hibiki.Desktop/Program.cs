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
        AppBuilder.Configure<Hibiki.App.App>().UsePlatformDetect().WithInterFont().StartWithClassicDesktopLifetime(args);
    }
}
