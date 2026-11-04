using Android.App;
using Android.Content.PM;
using Avalonia.Android;
using Hibike.App;

namespace Hibike.Android;
[Activity(Label = "Hibike", Theme = "@style/HibikeTheme", Icon = "@drawable/hibike_launcher", MainLauncher = true, LaunchMode = LaunchMode.SingleTop,
    ScreenOrientation = ScreenOrientation.Portrait,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public sealed class MainActivity : AvaloniaMainActivity
{
    private GoogleAccountLogin? _googleLogin;
    private WebAccountLogin? _webLogin;
    protected override void OnCreate(global::Android.OS.Bundle? savedInstanceState)
    {
        // Avalonia may attach MainView during base.OnCreate; persistence must already be ready.
        WebSessionStore.Configure(this);
        AppServices.ReadLocalMetadata = AndroidMetadata.Read;
        AppServices.ReadMediaInfo = AndroidMetadata.DetailsAsync;
        base.OnCreate(savedInstanceState);
        _googleLogin = new GoogleAccountLogin(this);
        _webLogin = new WebAccountLogin(this);
        AppServices.ConnectYouTubeAccount = _webLogin.ConnectAsync;
        OnBackPressedDispatcher.AddCallback(this, new BackCallback(this));
#if HIBIKE_SMOKE_TEST
        if (Intent?.GetBooleanExtra("hibike.smoke.playback", false) == true) { Intent.RemoveExtra("hibike.smoke.playback"); _ = PlaybackProbe.RunAsync(this); }
#endif
        if (Intent?.GetBooleanExtra("open_player", false) == true)
            Avalonia.Threading.Dispatcher.UIThread.Post(() => AppServices.OpenPlayerView?.Invoke());
        if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.Tiramisu)
            RequestPermissions(new[] { global::Android.Manifest.Permission.PostNotifications }, 42);
    }
    protected override void OnActivityResult(int requestCode, Result resultCode, global::Android.Content.Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        _googleLogin?.HandleResult(requestCode, resultCode, data);
    }
    protected override void OnStop()
    {
        AppServices.Controller?.SaveSession();
        AppServices.Controller?.FlushPendingSaves();
        base.OnStop();
    }
    protected override void OnDestroy()
    {
        _googleLogin?.Cancel();
        _webLogin?.Cancel();
        AppServices.ConnectYouTubeAccount = null;
        base.OnDestroy();
    }
    protected override void OnNewIntent(global::Android.Content.Intent? intent)
    {
        base.OnNewIntent(intent);
        if (intent?.GetBooleanExtra("open_player", false) == true)
            Avalonia.Threading.Dispatcher.UIThread.Post(() => AppServices.OpenPlayerView?.Invoke());
    }
    private sealed class BackCallback(MainActivity activity) : AndroidX.Activity.OnBackPressedCallback(true)
    {
        public override void HandleOnBackPressed()
        {
            if (AppServices.BackHandler?.Invoke() == true) return;
            Enabled = false;
            try { activity.OnBackPressedDispatcher.OnBackPressed(); }
            finally { Enabled = true; }
        }
    }
}
