using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using Hibike.App;
using Hibike.Core.Services;

namespace Hibike.Android;
[Application]
public sealed class Application(nint handle, JniHandleOwnership ownership)
    : AvaloniaAndroidApplication<Hibike.App.App>(handle, ownership)
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        var context = global::Android.App.Application.Context;
        AppServices.Configure(new YtDlpCatalog(new AndroidCommandRunner(context), context.FilesDir!.AbsolutePath),
            new AndroidAudioPlayer(context), context.FilesDir!.AbsolutePath);
        AppServices.RequestDisplayFrame = new AndroidDisplayFrames().Post;
        return base.CustomizeAppBuilder(builder).WithInterFont();
    }
}
