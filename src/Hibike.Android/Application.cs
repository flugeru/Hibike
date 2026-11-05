using Android.App;
using Android.Runtime;
using Android.Content;
using Android.Net;
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
        AppServices.IsNetworkAvailable = () =>
        {
            var connectivity = context.GetSystemService(Context.ConnectivityService) as ConnectivityManager;
            if (connectivity is null) return true;
            using var network = connectivity.ActiveNetwork;
            if (network is null) return false;
            using var capabilities = connectivity.GetNetworkCapabilities(network);
            return capabilities?.HasCapability(NetCapability.Validated) == true;
        };
        AppServices.RequestDisplayFrame = new AndroidDisplayFrames().Post;
        return base.CustomizeAppBuilder(builder).WithInterFont();
    }
}
