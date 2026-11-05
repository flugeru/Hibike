using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Hibike.App;
using Hibike.App.Views;
using Hibike.Core.Models;

internal static partial class Program
{
    private static async Task RunOptimizationChecksAsync(MainView view, Window window)
    {
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Field(string name) => typeof(MainView).GetField(name, hidden)!.GetValue(view);
        using var results = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "android-ui-study.json")));
        var root = results.RootElement;
        Check(root.GetProperty("distinctDecodedBitmapInstances").GetInt32() == 1, "Identical covers did not share their bitmap");
        Check(root.GetProperty("bitmapsReplacedAfterReattach").GetInt32() == 0, "Reattachment discarded cached covers");
        Check(root.GetProperty("homeArtworksAttached").GetInt32() < 50, "Home realized most offscreen covers");
        Check(root.GetProperty("homeFingerprintMissUnchangedShelves").GetProperty("medianAllocatedBytes").GetInt64() < 2_000_000,
            "Unchanged shelves allocated a new set of cover controls");

        var home = (ItemsControl)Field("_home")!;
        var homeScroll = (ScrollViewer)Field("_homeScroll")!;
        var shelves = home.Items.OfType<StackPanel>().Where(c => c.Tag is ValueTuple<string, string>).ToArray();
        foreach (var width in new[] { 360, 390, 430 })
        {
            window.Width = width; await Task.Delay(200);
            // Each shelf must still be reachable vertically and at both horizontal ends.
            shelves = home.Items.OfType<StackPanel>().Where(c => c.Tag is ValueTuple<string, string>).ToArray();
            foreach (var shelf in shelves)
            {
                home.ScrollIntoView(home.Items.IndexOf(shelf)); await Task.Delay(70);
                var scroll = shelf.Children.OfType<ScrollViewer>().Single();
                var items = (ItemsControl)scroll.Content!;
                foreach (var end in new[] { false, true })
                {
                    scroll.Offset = new Vector(end ? Math.Max(0, scroll.Extent.Width - scroll.Viewport.Width) : 0, 0);
                    await Task.Delay(70);
                    Check(scroll.Bounds.Height >= 160, "A virtualized shelf collapsed while scrolling");
                    var cards = items.GetVisualDescendants().OfType<Grid>().Where(c => c.Tag is Track).ToArray();
                    Check(cards.Length > 0, "A virtualized shelf became blank while scrolling");
                    var expected = items.Items[end ? items.ItemCount - 1 : 0]!;
                    var expectedTrack = (Track)expected.GetType().GetProperty("Track")!.GetValue(expected)!;
                    Check(cards.Any(c => ((Track)c.Tag!).Source == expectedTrack.Source), "Carousel did not realize its end item");
                    foreach (var card in cards)
                    {
                        var art = card.GetVisualDescendants().OfType<Control>().First(c => c.GetType().Name == "Artwork");
                        Check(art.Width == 160 && art.Height == 160, "Carousel altered cover dimensions");
                    }
                }
            }
            Capture(view, "optimized-home-" + width);
        }
        window.Height = 500;
        homeScroll.Offset = default;
        await Task.Delay(100);
        var firstShelf = shelves[0];
        var firstCarousel = firstShelf.Children.OfType<ScrollViewer>().Single();
        firstCarousel.Offset = new Vector(180, 0);
        await Task.Delay(100);
        var savedOffset = firstCarousel.Offset.X;
        Check(savedOffset > 0, "First shelf could not scroll horizontally");
        homeScroll.Offset = new Vector(0, homeScroll.Extent.Height);
        await Task.Delay(100);
        var realized = home.GetRealizedContainers().Select(home.IndexFromContainer).ToArray();
        Check(!realized.Contains(home.Items.IndexOf(firstShelf)),
            $"First shelf still realized at offset {homeScroll.Offset}, extent {homeScroll.Extent}, viewport {homeScroll.Viewport}, realized [{string.Join(",", realized)}]");
        homeScroll.Offset = default;
        await Task.Delay(100);
        Check(Math.Abs(firstCarousel.Offset.X - savedOffset) < 1, "Virtualized shelf lost its horizontal position");

        window.Height = 844;
        typeof(MainView).GetField("_page", hidden)!.SetValue(view, "library");
        typeof(MainView).GetMethod("Refresh", hidden)!.Invoke(view, null);
        await Task.Delay(100);
        var library = (ItemsControl)Field("_mobileLibraryTracks")!;
        var libraryScroll = (ScrollViewer)Field("_mobileLibraryScroll")!;
        libraryScroll.Offset = new Vector(0, 15 * 64 + 15);
        await Task.Delay(100);
        var top = library.GetRealizedContainers()
            .Select(row => (Row: row, Y: row.TranslatePoint(default, libraryScroll)?.Y ?? double.MinValue))
            .Where(entry => entry.Y + entry.Row.Bounds.Height > 0).OrderBy(entry => entry.Y).First();
        var anchor = (Track)library.Items[library.IndexFromContainer(top.Row)]!;
        var order = typeof(MainView).GetField("_mobileLibraryOrder", hidden)!;
        order.SetValue(view, Enum.Parse(order.FieldType, "Title"));
        typeof(MainView).GetMethod("RefreshMobileLibrary", hidden)!.Invoke(view, null);
        await Task.Delay(150);
        var anchorIndex = library.Items.IndexOf(anchor);
        Check(library.ContainerFromIndex(anchorIndex)?.TranslatePoint(default, libraryScroll) is { } anchored
            && Math.Abs(anchored.Y - top.Y) < 2, "Library ordering moved the visible track anchor");

        var music = AppServices.Controller!;
        var personal = (MusicController.Personalization)typeof(MusicController).GetField("_personal", hidden)!.GetValue(music)!;
        personal.Listening["snapshot-check"] = new() { Seconds = 42, Plays = 1 };
        music.SetReduceMotion(true);
        music.SaveSession(); personal.Listening["snapshot-check"].Seconds = 999;
        await music.FlushPendingSavesAsync();
        using (var saved = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(music.DataDirectory, "personalization.json"))))
        {
            Check(saved.RootElement.GetProperty("ReduceMotion").GetBoolean(), "Reduced motion preference was not persisted");
            Check(saved.RootElement.GetProperty("Listening").GetProperty("snapshot-check").GetProperty("Seconds").GetDouble() == 42,
                "Background writer read mutable stats after snapshot capture");
        }

        var artworkType = typeof(MainView).Assembly.GetType("Hibike.App.Views.Artwork")!;
        var bitmapField = artworkType.GetField("_bitmap", hidden)!;
        var local = new Track("quality", "Quality", "Offline", "quality", Thumbnail: new Uri(Path.Combine(music.DataDirectory, "offline-cover.png")).AbsoluteUri);
        Control Art(bool full) => (Control)Activator.CreateInstance(artworkType, [local, 160d, full, Stretch.UniformToFill, false, true])!;
        var small = Art(false); var full = Art(true); var cancelled = Art(true);
        var panel = new StackPanel(); panel.Children.Add(small); panel.Children.Add(full); panel.Children.Add(cancelled);
        var testWindow = new Window { Width = 390, Height = 844, Content = panel }; testWindow.Show();
        panel.Children.Remove(cancelled);
        for (var i = 0; i < 100 && (bitmapField.GetValue(small) is null || bitmapField.GetValue(full) is null); i++) await Task.Delay(20);
        var thumb = (Bitmap?)bitmapField.GetValue(small); var player = (Bitmap?)bitmapField.GetValue(full);
        Check(thumb is not null && player is not null, "A cancelled consumer interrupted another cover request");
        Check(!ReferenceEquals(thumb, player) && player!.PixelSize.Width > thumb!.PixelSize.Width,
            "Full-resolution artwork reused an insufficient thumbnail");
        panel.Children.Remove(small);
        using (var encoded = new MemoryStream()) player!.Save(encoded, new PngBitmapEncoderOptions());
        testWindow.Close();
        var deferred = Art(false);
        artworkType.GetProperty("DeferOffscreen")!.SetValue(deferred, true);
        var deferredScroll = new ScrollViewer
        {
            Content = new StackPanel { Children = { new Border { Height = 1000 }, deferred } }
        };
        var deferredWindow = new Window { Width = 390, Height = 500, Content = deferredScroll };
        deferredWindow.Show();
        try
        {
            await Task.Delay(100);
            Check(bitmapField.GetValue(deferred) is null, "An offscreen cover was loaded before it approached the viewport");
            deferredScroll.Offset = new Vector(0, 1000);
            for (var i = 0; i < 100 && bitmapField.GetValue(deferred) is null; i++) await Task.Delay(20);
            Check(ReferenceEquals(thumb, bitmapField.GetValue(deferred)), "A visible cover failed to reuse the full-quality card bitmap");
        }
        finally { deferredWindow.Close(); }

        music.SaveSession(); await music.FlushPendingSavesAsync();
    }
}
