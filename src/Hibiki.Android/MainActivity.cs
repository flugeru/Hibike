using Android.App;
using Android.Content.PM;
using Avalonia.Android;
using Hibiki.App;

namespace Hibiki.Android;
[Activity(Label = "Hibiki", Theme = "@style/HibikiTheme", Icon = "@drawable/hibiki_icon", MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public sealed class MainActivity : AvaloniaMainActivity
{
    protected override void OnCreate(global::Android.OS.Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
#if HIBIKI_SMOKE_TEST
        if (Intent?.GetBooleanExtra("hibiki.smoke.playback", false) == true) _ = PlaybackProbe.RunAsync(this);
#endif
        if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.Tiramisu)
            RequestPermissions(new[] { global::Android.Manifest.Permission.PostNotifications }, 42);
    }
    public override void OnBackPressed()
    {
        if (AppServices.BackHandler?.Invoke() == true) return;
        base.OnBackPressed();
    }
}
