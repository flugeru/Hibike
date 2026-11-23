using System.Reflection;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Hibike.App;
using Hibike.App.Views;
using Hibike.Core.Models;
using Hibike.Core.Services;

internal static partial class Program
{
    private static async Task CheckThemesAsync(string directory, Window window, bool capture)
    {
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        var music = AppServices.Controller!;
        var theme = typeof(MainView).Assembly.GetType("Hibike.App.Views.UiTheme")!;
        string Accent() => (string)theme.GetProperty("Accent")!.GetValue(null)!;
        var palette = typeof(MainView).Assembly.GetType("Hibike.App.Views.CoverPalette")!;
        object? Palette(byte r, byte g, byte b) => palette.GetMethod("FromBgra", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [Enumerable.Range(0, 64).SelectMany(_ => new[] { b, g, r, (byte)255 }).ToArray()]);

        var original = music.Theme;
        try
        {
            // Monochrome stays the default for new installations.
            var fresh = Path.Combine(directory, "theme-default");
            var defaults = new MusicController(new Catalog(), new Player(), new LibraryStore(fresh), fresh);
            Check(defaults.Theme == "Monocromático", "Monochrome is not the default theme");
            defaults.StopObserving();

            var view = (MainView)Activator.CreateInstance(typeof(MainView), hidden, null, [music, true], null)!;
            var previous = window.Content;
            window.Width = 390; window.Height = 844; window.Content = view;
            await Task.Delay(200);
            music.SetTheme("Monocromático");
            Check(Accent() == "#F2F2F2", "Monochrome theme changed the accent");
            music.SetTheme("Lavanda");
            Check(Accent() == "#B784F7", "Lavender theme did not apply its accent live");
            var selectedTab = view.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("nav") && b.Classes.Contains("selected"));
            var tabIcon = selectedTab.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().First();
            Check(tabIcon.Stroke is ISolidColorBrush { Color: var tabColor } && tabColor == Color.Parse("#B784F7"),
                "Selected tab did not adopt the new accent");
            if (capture) { await Task.Delay(100); Capture(view, "theme-lavanda-home"); }
            music.SetTheme("Rosa sakura");
            Check(Accent() == "#F7B7E3", "Sakura theme did not apply its accent live");

            // The preference survives a restart; an unknown stored value falls back instead of failing.
            music.StopObserving(); await music.FlushPendingSavesAsync(); music.StartObserving();
            var restored = new MusicController(new Catalog(), new Player(), new LibraryStore(directory), directory);
            Check(restored.Theme == "Rosa sakura", "Theme preference did not survive restart");
            restored.StopObserving();
            var odd = Path.Combine(directory, "theme-odd");
            Directory.CreateDirectory(odd);
            File.WriteAllText(Path.Combine(odd, "personalization.json"), "{\"Theme\":\"Neon\"}");
            var fallback = new MusicController(new Catalog(), new Player(), new LibraryStore(odd), odd);
            Check(fallback.Theme == "Monocromático", "Unknown stored theme was not reset to monochrome");
            fallback.StopObserving();

            // Cover colours: a vivid cover yields a light accent of its hue; a grey one keeps monochrome.
            Check(Palette(128, 128, 128) is null, "Grey cover produced a coloured accent");
            var red = Palette(200, 30, 30)!;
            var accent = Color.Parse((string)red.GetType().GetProperty("Accent")!.GetValue(red)!);
            Check(accent.R > accent.G && accent.R > accent.B && (accent.R + accent.G + accent.B) / 3 > 140,
                "Red cover did not yield a light red accent");

            // A real decoded cover drives the accent and the player wash.
            var coverPath = Path.Combine(directory, "theme-cover.png");
            var art = new Border { Width = 96, Height = 96, Background = new SolidColorBrush(Color.Parse("#E0701E")) };
            art.Measure(new Avalonia.Size(96, 96)); art.Arrange(new Avalonia.Rect(0, 0, 96, 96));
            using (var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new Avalonia.PixelSize(96, 96)))
            { bitmap.Render(art); bitmap.Save(coverPath, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default); }
            var previousCurrent = music.Current;
            typeof(MusicController).GetProperty("Current")!.SetValue(music,
                new Track("theme-cover", "Cover theme", "Artist", "theme-cover-source", 200, new Uri(coverPath).AbsoluteUri));
            try
            {
                music.SetTheme("Monocromático");
                music.SetTheme("Cor da capa");
                typeof(MainView).GetMethod("Refresh", hidden)!.Invoke(view, null);
                for (var i = 0; i < 150 && Accent() == "#F2F2F2"; i++) await Task.Delay(20);
                var coverAccent = Color.Parse(Accent());
                Check(coverAccent.R > coverAccent.G && coverAccent.G > coverAccent.B, $"Orange cover did not set an orange accent ({Accent()})");
                typeof(MainView).GetMethod("ShowPlayer", hidden)!.Invoke(view, null);
                await Task.Delay(300);
                var page = (Border)typeof(MainView).GetField("_playerPage", hidden)!.GetValue(view)!;
                Check(page.Background is LinearGradientBrush, "Cover theme did not tint the expanded player");
                if (capture) Capture(view, "theme-cover-player");
                AppServices.BackHandler!();
                music.SetTheme("Lavanda");
                Check(typeof(MainView).GetField("_playerTint", hidden)!.GetValue(view) is null && Accent() == "#B784F7",
                    "Leaving the cover theme kept its colours");
            }
            finally { typeof(MusicController).GetProperty("Current")!.SetValue(music, previousCurrent); }
            music.SetTheme("Cor da capa");
            if (capture)
            {
                typeof(MainView).GetMethod("ShowSettings", hidden)!.Invoke(view, null);
                await Task.Delay(200);
                Capture(view, "theme-settings");
            }
            window.Content = previous;
        }
        finally { music.SetTheme(original); }
    }
}
