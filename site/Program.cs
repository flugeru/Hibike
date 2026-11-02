using Avalonia;
using Avalonia.Browser;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace Hibike.Web;

internal static class Program
{
    private static Task Main() => AppBuilder.Configure<SiteApp>().WithInterFont().StartBrowserAppAsync("out");
}

public sealed class SiteApp : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is ISingleViewApplicationLifetime browser) browser.MainView = new LandingView();
        base.OnFrameworkInitializationCompleted();
    }
}

internal sealed class LandingView : UserControl
{
    public LandingView()
    {
        Background = Brushes.Black;
        FontFamily = new FontFamily("avares://Avalonia.Fonts.Inter/Assets#Inter");
        var content = new StackPanel { Spacing = 28, MaxWidth = 1080, Margin = new Thickness(24), HorizontalAlignment = HorizontalAlignment.Stretch };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        brand.Children.Add(new Image { Source = new Bitmap(AssetLoader.Open(new Uri("avares://Hibike.Web/Assets/hibike.png"))), Width = 44, Height = 44 });
        brand.Children.Add(Label("Hibike", 28, true)); content.Children.Add(brand);
        content.Children.Add(Label("YOUR MUSIC. YOUR RHYTHM.", 13, color: "#AAAAAA"));
        var heading = Label("Less noise.\nMore music.", 64, true); content.Children.Add(heading);
        content.Children.Add(Label("A pure-black music experience for desktop and Android. Local favorites, online discovery and your next great listen — together.", 20, color: "#BBBBBB"));
        var actions = new WrapPanel { ItemSpacing = 12, LineSpacing = 12 };
        actions.Children.Add(Link("Explore the project", "https://github.com/flugeru/Hibike", true));
        actions.Children.Add(Link("Development builds", "https://github.com/flugeru/Hibike/actions")); content.Children.Add(actions);
        var features = new WrapPanel { ItemSpacing = 16, LineSpacing = 16 };
        foreach (var (title, description) in new[]
        {
            ("Pure black. Clear focus.", "AMOLED surfaces, quiet controls and an adaptive interface that puts music first."),
            ("Your collection, together.", "Play local audio, keep favorites and shape your queue. Features vary by build."),
            ("Connect on your terms.", "Optional Google library access, read-only. Local listening needs no Google account.")
        })
        {
            var body = new StackPanel { Spacing = 12 }; body.Children.Add(Label(title, 20, true)); body.Children.Add(Label(description, 15, color: "#AAAAAA"));
            features.Children.Add(new Border { Width = 310, Padding = new Thickness(24), CornerRadius = new(16), Background = Brush.Parse("#0D0D0D"), BorderBrush = Brush.Parse("#292929"), BorderThickness = new(1), Child = body });
        }
        content.Children.Add(features);
        content.Children.Add(Label("Built in the open", 28, true));
        content.Children.Add(Label("Made with C#, .NET and Avalonia UI. This page runs real Avalonia through WebAssembly. Hibike is in active development; these links lead to development builds, not stable releases.", 16, color: "#AAAAAA"));
        var legal = new WrapPanel { ItemSpacing = 12, LineSpacing = 12 };
        legal.Children.Add(Link("Privacy Policy", "https://flugeru.github.io/Hibike/privacy.html"));
        legal.Children.Add(Link("Terms of Service", "https://flugeru.github.io/Hibike/terms.html"));
        legal.Children.Add(Link("Contact", "https://github.com/flugeru/Hibike/issues")); content.Children.Add(legal);
        Content = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        SizeChanged += (_, _) =>
        {
            heading.FontSize = Bounds.Width < 600 ? 44 : 64;
            foreach (var card in features.Children) card.Width = Math.Min(310, Math.Max(0, Bounds.Width - 48));
        };
    }

    private static TextBlock Label(string text, double size, bool bold = false, string color = "#FFFFFF") => new()
    {
        Text = text, FontSize = size, FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
        Foreground = Brush.Parse(color), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center
    };

    private Button Link(string text, string url, bool primary = false)
    {
        var button = new Button
        {
            Content = text, MinHeight = 48, Padding = new Thickness(20, 12), CornerRadius = new(24),
            Background = primary ? Brushes.White : Brushes.Black, Foreground = primary ? Brushes.Black : Brushes.White,
            BorderBrush = Brush.Parse("#383838"), BorderThickness = new(1)
        };
        button.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.Launcher is { } launcher) await launcher.LaunchUriAsync(new Uri(url));
        };
        return button;
    }
}
