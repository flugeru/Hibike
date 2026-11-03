using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Hibike.App.Views;
namespace Hibike.App;

public sealed partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        UiTheme.InstallResources(Resources);
        UiTheme.Install(Styles);
    }
    public override void OnFrameworkInitializationCompleted()
    {
        var view = new MainView(AppServices.CreateController());
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new Window
            {
                Title = "Hibike • by Flugel", Width = 1260, Height = 820,
                Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://Hibike.App/Assets/hibike-desktop.ico"))),
                MinWidth = 360, MinHeight = 600, Content = view,
                Background = UiTheme.Brush(UiTheme.Background)
            };
            desktop.Exit += (_, _) => { AppServices.Controller?.SaveSession(); AppServices.Controller?.FlushPendingSaves(); AppServices.Player?.Dispose(); };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime mobile) mobile.MainView = view;
        base.OnFrameworkInitializationCompleted();
    }
}
