using Android.App;
using Android.Content.PM;
using Avalonia.Android;
using Hibiki.App;

namespace Hibiki.Android;
[Activity(Label = "Hibiki", Theme = "@style/HibikiTheme", Icon = "@drawable/hibiki_icon", MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public sealed class MainActivity : AvaloniaMainActivity
{
#if HIBIKI_SMOKE_TEST
    protected override void OnCreate(global::Android.OS.Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        if (Intent?.GetBooleanExtra("hibiki.smoke.playback", false) == true)
            _ = PlaybackProbe.RunAsync(this);
    }
#endif
    // The first milestone deliberately plays in the foreground only.
    protected override void OnStop()
    {
        AppServices.Suspend();
        base.OnStop();
    }
}
