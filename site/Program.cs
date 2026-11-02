using System.Globalization;
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
    private bool pt = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "pt";
    private readonly List<(TextBlock block, string en, string br)> localized = new();
    private TextBlock T(string en, string br, double size, bool bold = false, string color = "#FFFFFF")
    {
        var block = Label(pt ? br : en, size, bold, color);
        localized.Add((block, en, br));
        return block;
    }
    public LandingView()
    {
        Background = Brushes.Black;
        FontFamily = new FontFamily("avares://Avalonia.Fonts.Inter/Assets#Inter");
        var content = new StackPanel { Spacing = 28, MaxWidth = 1080, Margin = new Thickness(24), HorizontalAlignment = HorizontalAlignment.Stretch };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        brand.Children.Add(new Image { Source = new Bitmap(AssetLoader.Open(new Uri("avares://Hibike.Web/Assets/hibike.png"))), Width = 44, Height = 44 });
        brand.Children.Add(Label("Hibike", 28, true));
        var switchLanguage = new Button { Content = pt ? "EN" : "PT-BR", MinWidth = 68, MinHeight = 36, Padding = new Thickness(14, 7), CornerRadius = new CornerRadius(18), Background = Brush.Parse("#0D0D0D"), Foreground = Brushes.White, BorderBrush = Brush.Parse("#292929"), BorderThickness = new Thickness(1), FontSize = 12, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        switchLanguage.Click += (_, _) => { pt = !pt; switchLanguage.Content = pt ? "EN" : "PT-BR"; foreach (var (block, en, br) in localized) block.Text = pt ? br : en; };
        brand.Children.Add(switchLanguage); content.Children.Add(brand);
        content.Children.Add(T("YOUR MUSIC. YOUR RHYTHM.", "SUA MÚSICA. SEU RITMO.", 13, color: "#AAAAAA"));
        var heading = T("Less noise.\nMore music.", "Menos ruído.\nMais música.", 64, true); content.Children.Add(heading);
        content.Children.Add(T("A pure-black music experience for desktop and Android. Local favorites, online discovery and your next great listen — together.", "Uma experiência musical em preto puro para desktop e Android. Favoritos locais, descobertas online e sua próxima música favorita — tudo junto.", 20, color: "#BBBBBB"));
        var actions = new WrapPanel { ItemSpacing = 12, LineSpacing = 12 };
        actions.Children.Add(Link(pt ? "Explorar o projeto" : "Explore the project", "https://github.com/flugeru/Hibike", true));
        actions.Children.Add(Link(pt ? "Versões de desenvolvimento" : "Development builds", "https://github.com/flugeru/Hibike/actions")); content.Children.Add(actions);
        var features = new WrapPanel { ItemSpacing = 16, LineSpacing = 16 };
        foreach (var (title, description, titlePt, descriptionPt) in new[]
        {
            ("Pure black. Clear focus.", "AMOLED surfaces, quiet controls and an adaptive interface that puts music first.", "Preto puro. Foco total.", "Superfícies AMOLED, controles discretos e uma interface que coloca a música em primeiro lugar."),
            ("Your collection, together.", "Play local audio, keep favorites and shape your queue. Features vary by build.", "Sua coleção, reunida.", "Reproduza músicas locais, salve favoritos e organize sua fila. Os recursos variam conforme a versão."),
            ("Connect on your terms.", "Optional Google library access, read-only. Local listening needs no Google account.", "Conecte-se do seu jeito.", "Acesso opcional à biblioteca do Google, somente leitura. A reprodução local não exige conta Google.")
        })
        {
            var body = new StackPanel { Spacing = 12 }; body.Children.Add(T(title, titlePt, 20, true)); body.Children.Add(T(description, descriptionPt, 15, color: "#AAAAAA"));
            features.Children.Add(new Border { Width = 310, Padding = new Thickness(24), CornerRadius = new(16), Background = Brush.Parse("#0D0D0D"), BorderBrush = Brush.Parse("#292929"), BorderThickness = new(1), Child = body });
        }
        content.Children.Add(features);
        content.Children.Add(T("Built in the open", "Código aberto", 28, true));
        content.Children.Add(T("Made with C#, .NET and Avalonia UI. This page runs real Avalonia through WebAssembly. Hibike is in active development; these links lead to development builds, not stable releases.", "Feito com C#, .NET e Avalonia UI. Esta página executa Avalonia via WebAssembly. O Hibike está em desenvolvimento; os links levam a versões de teste, não a lançamentos estáveis.", 16, color: "#AAAAAA"));
        var legal = new WrapPanel { ItemSpacing = 12, LineSpacing = 12 };
        legal.Children.Add(Link(pt ? "Política de Privacidade" : "Privacy Policy", "https://flugeru.github.io/Hibike/privacy.html"));
        legal.Children.Add(Link(pt ? "Termos de Serviço" : "Terms of Service", "https://flugeru.github.io/Hibike/terms.html"));
        legal.Children.Add(Link(pt ? "Contato" : "Contact", "https://github.com/flugeru/Hibike/issues")); content.Children.Add(legal);
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
