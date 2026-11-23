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
        AppServices.IsNetworkAvailable = DefaultNetworkState.Create(context).IsAvailable;
        AppServices.RequestDisplayFrame = new AndroidDisplayFrames().Post;
        return base.CustomizeAppBuilder(builder).WithInterFont();
    }
}

// The UI and playback engine read connectivity many times per second. Querying
// ConnectivityManager each time cost several JNI calls and Java allocations per read; the
// system default-network callback keeps an equivalent cached answer and updates it on change.
internal sealed class DefaultNetworkState : ConnectivityManager.NetworkCallback
{
    private readonly ConnectivityManager? _connectivity;
    private readonly bool _registered;
    private volatile bool _available;

    private DefaultNetworkState(ConnectivityManager? connectivity)
    {
        _connectivity = connectivity;
        _available = Query();
        if (connectivity is null) return;
        try { connectivity.RegisterDefaultNetworkCallback(this); _registered = true; }
        catch (Java.Lang.Exception error)
        { PlaybackDiagnostics.Write("Rede Android", "Monitor de rede indisponível; consultando sob demanda. " + error.GetType().Name); }
        // A change between the first query and registration is covered by the callback's initial report.
    }

    public static DefaultNetworkState Create(Context context) =>
        new(context.GetSystemService(Context.ConnectivityService) as ConnectivityManager);

    public bool IsAvailable() => _registered ? _available : Query();

    private bool Query()
    {
        if (_connectivity is null) return true;
        using var network = _connectivity.ActiveNetwork;
        if (network is null) return false;
        using var capabilities = _connectivity.GetNetworkCapabilities(network);
        return capabilities?.HasCapability(NetCapability.Validated) == true;
    }

    public override void OnCapabilitiesChanged(Network network, NetworkCapabilities networkCapabilities) =>
        _available = networkCapabilities.HasCapability(NetCapability.Validated);

    public override void OnLost(Network network) => _available = false;
}
