using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Hibiki.App.Views;
namespace Hibiki.App;

public sealed partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        UiTheme.Install(Styles);
    }
    public override void OnFrameworkInitializationCompleted()
    {
        var view = new MainView(AppServices.CreateController());
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new Window
            {
                Title = "Hibiki • by Flugel", Width = 1260, Height = 820,
                MinWidth = 360, MinHeight = 600, Content = view,
                Background = UiTheme.Brush(UiTheme.Background)
            };
            desktop.Exit += (_, _) => AppServices.Player?.Dispose();
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime mobile) mobile.MainView = view;
        base.OnFrameworkInitializationCompleted();
    }
}
