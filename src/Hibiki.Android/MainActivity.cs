using Android.App;
using Android.Content.PM;
using Avalonia.Android;
using Hibiki.App;

namespace Hibiki.Android;
[Activity(Label = "Hibiki", Theme = "@style/HibikiTheme", Icon = "@drawable/hibiki_icon", MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public sealed class MainActivity : AvaloniaMainActivity
{
    // The first milestone deliberately plays in the foreground only.
    protected override void OnStop()
    {
        AppServices.Suspend();
        base.OnStop();
    }
}
