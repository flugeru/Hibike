using System.Reflection;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Hibike.App;
using Hibike.App.Views;
using Hibike.Core.Models;
using Hibike.Core.Services;

internal static partial class Program
{
    private static async Task CheckStorageAndMotionAsync(string directory, MainView view)
    {
        var track = new Track("favorite", "Song", "Artist", "https://example.org/favorite");
        var blocked = Path.Combine(directory, "blocked-favorites");
        File.WriteAllText(blocked, "not a directory");
        var controller = new MusicController(new Catalog(), new Player(), new LibraryStore(blocked), blocked);
        controller.StopObserving();
        await controller.ToggleFavoriteAsync(track);
        Check(!controller.IsFavorite(track) && controller.HasError, "Failed save added a favorite in memory");
        controller.Favorites.Add(track);
        await controller.ToggleFavoriteAsync(track);
        Check(controller.IsFavorite(track), "Failed save removed a favorite in memory");

        var profile = Path.Combine(directory, "corrupt-favorites");
        Directory.CreateDirectory(profile);
        const string broken = "{broken";
        File.WriteAllText(Path.Combine(profile, "favorites.json"), broken);
        new PlaybackSessionStore(profile).Save(new([track], track, 0, 12));
        controller = new MusicController(new Catalog(), new Player(), new LibraryStore(profile), profile);
        controller.StopObserving();
        await controller.InitializeAsync();
        Check(controller.Current == track && controller.Queue.Contains(track), "Broken favorites prevented session restore");
        Check(controller.HasError && controller.Status.Contains("favoritos"), "Session restore hid the favorites read error");
        await controller.ToggleFavoriteAsync(track);
        Check(!controller.IsFavorite(track) && File.ReadAllText(Path.Combine(profile, "favorites.json")) == broken,
            "Broken favorites were overwritten");
        await controller.FlushPendingSavesAsync();

        profile = Path.Combine(directory, "concurrent-favorites");
        controller = new MusicController(new Catalog(), new Player(), new LibraryStore(profile), profile);
        controller.StopObserving();
        await Task.WhenAll(controller.ToggleFavoriteAsync(track), controller.ToggleFavoriteAsync(track));
        Check(controller.Favorites.Count == 0 && (await new LibraryStore(profile).LoadAsync()).Count == 0,
            "Concurrent favorite toggles lost an update");

        profile = Path.Combine(directory, "invalid-personalization");
        Directory.CreateDirectory(profile);
        File.WriteAllText(Path.Combine(profile, "personalization.json"), "{\"Listening\":null}");
        File.WriteAllText(Path.Combine(profile, "playback-session.json"), "{\"Queue\":null}");
        controller = new MusicController(new Catalog(), new Player(), new LibraryStore(profile), profile);
        controller.StopObserving();
        await controller.InitializeAsync();
        Check(controller.OfflineTracks.Count == 0 && controller.Queue.Count == 0, "Invalid local data interrupted initialization");
        try { await controller.FlushPendingSavesAsync(); } catch (IOException) { }
        Check(File.ReadAllText(Path.Combine(profile, "personalization.json")) == "{\"Listening\":null}",
            "Invalid personalization was overwritten with defaults");

        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        var theme = typeof(MainView).Assembly.GetType("Hibike.App.Views.UiTheme")!;
        var carousel = (ScrollViewer)theme.GetMethod("FadingScroll")!.Invoke(null,
            [new Border { Width = 1200, Height = 80 }, true])!;
        carousel.Measure(new Avalonia.Size(360, 80));
        carousel.Arrange(new Avalonia.Rect(0, 0, 360, 80));
        carousel.Offset = new Avalonia.Vector(100, 0);
        Check(carousel.OpacityMask is null, "Carousel darkens artwork edges with a black strip");
        typeof(MainView).GetMethod("ShowDiagnostics", hidden)!.Invoke(view, [DiagnosticSector.Storage]);
        var sheet = (Border)typeof(MainView).GetField("_overlay", hidden)!.GetValue(view)!;
        Check(KeyboardNavigation.GetTabNavigation(sheet) == KeyboardNavigationMode.Cycle, "Sheet does not cycle keyboard focus");
        view.UpdateLayout();
        var copy = sheet.GetVisualDescendants().OfType<Button>().Where(b => b.Content as string is "Copiar setor" or "Copiar todos").ToArray();
        Check(copy.Length == 2 && Math.Abs(copy[0].Bounds.Width - copy[1].Bounds.Width) < 1,
            "Diagnostics actions have inconsistent widths");
        typeof(MainView).GetMethod("CloseSheet", hidden)!.Invoke(view, null);
        Check(!sheet.IsEnabled && !sheet.IsHitTestVisible, "Closing sheet accepts input during exit");
        await Task.Delay(220);
        Check(sheet.Parent is null, "Exit animation did not remove sheet");

        var motion = typeof(MainView).Assembly.GetType("Hibike.App.Views.UiMotion")!;
        var enter = motion.GetMethod("Enter")!;
        var button = new Button();
        motion.GetMethod("AttachPressFeedback")!.Invoke(null, [button]);
        AppServices.Controller!.SetReduceMotion(true);
        typeof(Button).GetProperty(nameof(Button.IsPressed))!.SetValue(button, true);
        Check(button.Transitions is null && button.RenderTransform is null, "Reduced motion still transforms buttons");
        var panel = new Border();
        enter.Invoke(null, [panel, 12d, false]);
        Check(panel.Opacity == 1 && panel.RenderTransform is null && panel.Transitions is null,
            "Reduced motion hides or animates page entrance");
        AppServices.Controller.SetReduceMotion(false);
        typeof(Button).GetProperty(nameof(Button.IsPressed))!.SetValue(button, false);
        typeof(Button).GetProperty(nameof(Button.IsPressed))!.SetValue(button, true);
        Check(button.Transitions?.Count == 1 && button.RenderTransform is not null,
            "Button press lacks transform feedback");
        enter.Invoke(null, [panel, 12d, false]);
        Check(panel.Opacity == 0 && panel.Transitions?.Count == 2, "Page entrance lacks fade and transform transitions");
        await Task.Delay(220);
        Check(panel.Opacity == 1, "Page remains invisible after entrance");
    }
}
