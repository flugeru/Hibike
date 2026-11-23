using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Hibike.App;
using Hibike.App.Views;
using Hibike.Core.Models;

internal static partial class Program
{
    private static async Task CheckAudioInfoAsync(MainView view, Window window)
    {
        window.Width = 390; window.Height = 820;
        var music = AppServices.Controller!;
        await music.InitializeAsync();
        var player = (Player)music.Player;
        player.Route = new(Device: "Fone de teste", Output: "Aparelho de teste", Connection: "Bluetooth",
            Format: "PCM · 48 kHz");
        var track = new Track("audio-info", "Primeira gravação", "Artista", "audio-info");
        player.Volume = .7;
        await music.PlayAsync(track);
        AppServices.OpenPlayerView!();
        await Task.Delay(250);
        var badge = view.GetVisualDescendants().OfType<Button>().First(b => b.IsEffectivelyVisible &&
            b.Content is TextBlock && AutomationProperties.GetName(b) == "Informações do áudio");
        badge.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var sheet = (Border)typeof(MainView).GetField("_overlay", System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic)!.GetValue(view)!;
        bool Has(string text) => sheet.GetVisualDescendants().OfType<TextBlock>().Any(t =>
            t.Text?.Contains(text, StringComparison.Ordinal) == true && t.IsEffectivelyVisible);
        async Task Expect(Func<bool> predicate, string message)
        {
            for (var i = 0; i < 60 && !predicate(); i++) await Task.Delay(100);
            Check(predicate(), message);
        }
        await Expect(() => Has("Fone de teste") && Has("Aparelho de teste") && Has("PCM · 48 kHz") &&
            Has("Compressão com perdas") && Has("160 kb/s"),
            "Source quality and output were not both displayed");
        Capture(view, "audio-info-mobile");
        player.Route = new(Device: "Caixa de teste");
        await Expect(() => Has("Caixa de teste") && !Has("Fone de teste"), "Output change left a stale route");
        await music.PlayAsync(track with { Id = "next", Source = "next", Title = "Segunda gravação" });
        await Expect(() => Has("Segunda gravação") && !Has("Primeira gravação"), "Track change left stale metadata");
        player.Route = new();
        await Expect(() => !Has("Caixa de teste"), "Unknown route retained previous device");
        Check(AppServices.BackHandler!(), "Audio info did not close");
        var reads = player.RouteReads;
        await Task.Delay(2600);
        Check(reads == player.RouteReads, "Output queried after sheet closed");
    }
}
