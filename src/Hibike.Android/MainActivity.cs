using Android.App;
using Android.Content.PM;
using Avalonia.Android;
using Hibike.App;

namespace Hibike.Android;
[Activity(Label = "Hibike", Theme = "@style/HibikeTheme", Icon = "@drawable/hibike_launcher", MainLauncher = true, LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public sealed class MainActivity : AvaloniaMainActivity
{
    protected override void OnCreate(global::Android.OS.Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        OnBackPressedDispatcher.AddCallback(this, new BackCallback(this));
#if HIBIKE_SMOKE_TEST
        if (Intent?.GetBooleanExtra("hibike.smoke.playback", false) == true) { Intent.RemoveExtra("hibike.smoke.playback"); _ = PlaybackProbe.RunAsync(this); }
#endif
        if (Intent?.GetBooleanExtra("open_player", false) == true)
            Avalonia.Threading.Dispatcher.UIThread.Post(() => AppServices.OpenPlayerView?.Invoke());
        if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.Tiramisu)
            RequestPermissions(new[] { global::Android.Manifest.Permission.PostNotifications }, 42);
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
