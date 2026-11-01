using Android.App;
using Android.Content.PM;
using Avalonia.Android;
using Hibiki.App;

namespace Hibiki.Android;
[Activity(Label = "Hibiki", Theme = "@style/HibikiTheme", Icon = "@drawable/hibiki_icon", MainLauncher = true, LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public sealed class MainActivity : AvaloniaMainActivity
{
    protected override void OnCreate(global::Android.OS.Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        OnBackPressedDispatcher.AddCallback(this, new BackCallback(this));
#if HIBIKI_SMOKE_TEST
        if (Intent?.GetBooleanExtra("hibiki.smoke.playback", false) == true) { Intent.RemoveExtra("hibiki.smoke.playback"); _ = PlaybackProbe.RunAsync(this); }
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
