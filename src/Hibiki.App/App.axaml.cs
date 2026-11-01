using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Hibiki.App.Views;

namespace Hibiki.App;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var controller = AppServices.CreateController();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new Window
            {
                Title = "Hibiki • by Flugel",
                Width = 1360, Height = 860,
                MinWidth = 860, MinHeight = 580,
                Content = new DesktopView(controller),
                Background = DesktopTheme.Background
            };
            desktop.Exit += (_, _) => AppServices.Player?.Dispose();
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime mobile)
            mobile.MainView = new MainView(controller);

        base.OnFrameworkInitializationCompleted();
    }
}
