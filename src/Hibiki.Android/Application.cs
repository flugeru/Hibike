using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using Hibiki.App;
using Hibiki.Core.Services;

namespace Hibiki.Android;
[Application]
public sealed class Application(nint handle, JniHandleOwnership ownership)
    : AvaloniaAndroidApplication<Hibiki.App.App>(handle, ownership)
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        var context = global::Android.App.Application.Context;
        AppServices.Configure(new YtDlpCatalog(new AndroidCommandRunner(context)),
            new AndroidAudioPlayer(context), context.FilesDir!.AbsolutePath);
        return base.CustomizeAppBuilder(builder).WithInterFont();
    }
}
