using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hibike.App;
using Hibike.App.Views;
using Hibike.Core.Models;
using Hibike.Core.Services;

internal static partial class Program
{
    // Diagnostic workload, not a device FPS benchmark. All catalogs and artwork are offline.
    private static async Task RunPerformanceStudyAsync(MainView view, Window window)
    {
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Field(string name) => typeof(MainView).GetField(name, hidden)!.GetValue(view);
        void Set(string name, object? value) => typeof(MainView).GetField(name, hidden)!.SetValue(view, value);
        void Call(string name) => typeof(MainView).GetMethod(name, hidden)!.Invoke(view, null);
        var music = AppServices.Controller!;
        music.StopObserving(); music.SetAutoplay(false);
        await music.InitializeAsync();
        window.Width = 390; window.Height = 844;
        await Task.Delay(150);
        var fixture = Enumerable.Range(0, 120).Select(i => new Track("perf-" + i,
            "Performance sample " + i, "Artist " + i % 12, "offline-track-" + i, 180,
            Album: "Album " + i % 8)).ToArray();
        foreach (var track in fixture.Take(60)) music.Favorites.Add(track);
        foreach (var track in fixture.Take(40)) music.History.Add(track);
        typeof(MusicController).GetField("<HomeRecommendations>k__BackingField", hidden)!.SetValue(music, fixture.Skip(60).Take(24).ToArray());
        foreach (var (name, chart) in new[]
        {
            ("RegionalChart", new MusicChart("BR", "Offline regional chart", DateTime.UtcNow, fixture.Skip(80).Take(20).ToArray())),
            ("GlobalChart", new MusicChart("ZZ", "Offline global chart", DateTime.UtcNow, fixture.Skip(90).Take(20).ToArray())),
            ("TrendingChart", new MusicChart("BR", "Offline trending chart", DateTime.UtcNow, fixture.Skip(100).Take(20).ToArray()))
        }) typeof(MusicController).GetField("<" + name + ">k__BackingField", hidden)!.SetValue(music, chart);
        music.ReplaceQueue(fixture.Take(10)); await music.PlayAsync(fixture[0], 0);
        Set("_page", "home"); Call("Refresh"); await Task.Delay(300);

        var results = new Dictionary<string, object>
        {
            ["scope"] = "Headless Skia, synthetic data at 390x844 DIP; not Android FPS or GPU measurements",
            ["framework"] = typeof(Control).Assembly.GetName().Version!.ToString(),
            ["runtime"] = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            ["referenceBaselineCommit"] = "818b8200cc465fe938a3087f9c2dce3b9843ecf7"
        };
        var home = (ItemsControl)Field("_home")!;
        var artworks = home.GetVisualDescendants().OfType<Control>().Where(c => c.GetType().Name == "Artwork").ToArray();
        var viewport = new Rect(0, 0, view.Bounds.Width, view.Bounds.Height);
        var intersecting = artworks.Count(c => c.TranslatePoint(default, view) is { } origin
            && new Rect(origin, c.Bounds.Size).Intersects(viewport));
        results["homeArtworksAttached"] = artworks.Length;
        results["homeArtworksIntersectingWindowUpperBound"] = intersecting;
        results["homeShelves"] = home.Items.OfType<StackPanel>().Count(c => c.Tag is ValueTuple<string, string>);
        results["homeFingerprintHit"] = Measure(() => Call("RefreshHome"));
        var shelves = home.Items.OfType<StackPanel>().Where(c => c.Tag is ValueTuple<string, string>).ToArray();
        results["homeFingerprintMissUnchangedShelves"] = Measure(() => { Set("_homeFingerprint", null); Call("RefreshHome"); });
        results["shelfInstancesRetained"] = shelves.Count(home.Items.Contains);
        results["idleRefreshPlayback"] = Measure(() => Call("RefreshPlayback"), 50);
        results["hiddenDesktopDockVisible"] = ((Control)Field("_desktopDock")!).IsVisible;
        results["hiddenDesktopDockRefresh"] = Measure(() => Call("RefreshDock"), 50);

        Set("_page", "explore");
        ((TextBox)Field("_search")!).Text = "synthetic";
        typeof(MusicController).GetField("<ResultsQuery>k__BackingField", hidden)!.SetValue(music, "synthetic");
        music.Results.Clear();
        foreach (var i in Enumerable.Range(0, 1000)) music.Results.Add(fixture[i % fixture.Length] with { Id = "list-" + i, Source = "list-" + i });
        Call("Refresh"); await Task.Delay(250);
        results["searchItems"] = music.Results.Count;
        results["searchRealizedRows"] = ((ItemsControl)Field("_mobileSearchItems")!).GetVisualDescendants().OfType<Button>()
            .Count(b => Avalonia.Automation.AutomationProperties.GetName(b)?.StartsWith("Opções de ") == true);
        results["stableSearchRefresh"] = Measure(() => Call("Refresh"), 20);

        Set("_page", "home"); Call("Refresh"); await Task.Delay(50);
        music.ReplaceQueue(fixture.Take(100));
        results["saveSession100Tracks"] = Measure(music.SaveSession, 10);
        var personal = (MusicController.Personalization)typeof(MusicController).GetField("_personal", hidden)!.GetValue(music)!;
        foreach (var i in Enumerable.Range(0, 1000)) personal.Listening["synthetic-" + i] = new() { Plays = 2, Seconds = 360, LastPlayed = DateTime.UtcNow };
        results["saveSession100Tracks1000Stats"] = Measure(music.SaveSession, 10);
        await music.FlushPendingSavesAsync();
        results["savedSessionBytes"] = new FileInfo(Path.Combine(music.DataDirectory, "playback-session.json")).Length;
        results["savedPreferencesBytes"] = new FileInfo(Path.Combine(music.DataDirectory, "personalization.json")).Length;
        results["saveSnapshotCapturedOnUiThread"] = Dispatcher.UIThread.CheckAccess();

        var artworkType = typeof(MainView).Assembly.GetType("Hibike.App.Views.Artwork")!;
        var bitmapField = artworkType.GetField("_bitmap", hidden)!;
        var asset = Path.Combine(music.DataDirectory, "offline-cover.png");
        using (var input = Avalonia.Platform.AssetLoader.Open(new Uri("avares://Hibike.App/Assets/hibike-desktop.png")))
        using (var output = File.Create(asset)) await input.CopyToAsync(output);
        var imageTrack = fixture[0] with { Thumbnail = new Uri(asset).AbsoluteUri };
        var images = Enumerable.Range(0, 8).Select(_ => (Control)Activator.CreateInstance(artworkType,
            [imageTrack, 160d, false, Stretch.UniformToFill, false, true])!).ToArray();
        var imagePanel = new WrapPanel(); foreach (var image in images) imagePanel.Children.Add(image);
        var imageWindow = new Window { Width = 390, Height = 844, Content = imagePanel };
        imageWindow.Show();
        async Task AwaitImages()
        {
            var limit = Stopwatch.StartNew();
            while (images.Any(i => bitmapField.GetValue(i) is null))
            { if (limit.Elapsed.TotalSeconds > 10) throw new Exception("Offline cover decoding timed out"); await Task.Delay(20); }
        }
        await AwaitImages();
        var decoded = images.Select(i => bitmapField.GetValue(i)!).ToArray();
        results["identicalArtworkControls"] = images.Length;
        results["distinctDecodedBitmapInstances"] = decoded.Distinct(ReferenceEqualityComparer.Instance).Count();
        imagePanel.Children.Clear(); foreach (var image in images) imagePanel.Children.Add(image);
        await AwaitImages();
        results["bitmapsReplacedAfterReattach"] = images.Select((i, n) => !ReferenceEquals(decoded[n], bitmapField.GetValue(i))).Count(x => x);
        imageWindow.Close();

        var json = JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
        var destination = Path.Combine(AppContext.BaseDirectory, "android-ui-study.json");
        File.WriteAllText(destination, json); Console.WriteLine(json);
        music.StopObserving(); await music.FlushPendingSavesAsync();
    }

    private static object Measure(Action action, int count = 8)
    {
        action(); // JIT/cache warmup is excluded.
        var times = new List<double>(); var allocations = new List<long>();
        for (var i = 0; i < count; i++)
        {
            var bytes = GC.GetAllocatedBytesForCurrentThread(); var start = Stopwatch.GetTimestamp();
            action();
            times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            allocations.Add(GC.GetAllocatedBytesForCurrentThread() - bytes);
        }
        times.Sort(); allocations.Sort();
        return new { samples = count, medianMs = times[count / 2], maxMs = times[^1], medianAllocatedBytes = allocations[count / 2] };
    }
}
