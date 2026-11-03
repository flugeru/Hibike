using System.Reflection;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Hibike.App;
using Hibike.App.Views;
using Hibike.Core.Models;
using Hibike.Core.Services;

internal static partial class Program
{
    private static async Task RunQueueRegressionChecksAsync(string directory, MainView view, Window window)
    {
        Track Song(string id) => new(id, "Melody " + id, "Artist " + id, "https://example.org/" + id, 180);
        MusicController Controller(QueueCatalog catalog)
        {
            var path = Path.Combine(directory, Guid.NewGuid().ToString("N"));
            var music = new MusicController(catalog, new Player(), new LibraryStore(path), path);
            music.StopObserving(); music.SetAutoplay(false);
            return music;
        }
        var catalog = new QueueCatalog();
        var radio = Controller(catalog);
        await radio.RetryRadioAsync();
        Check(catalog.RadioCalls == 0 && !radio.RadioLoading, "Radio queried without a seed");
        var seed = Song("seed");
        await radio.PlayAsync(seed);
        catalog.Fail = true;
        await radio.RetryRadioAsync();
        Check(!radio.RadioLoading && radio.Queue.SequenceEqual([seed]), "Failed radio damaged queue or stayed loading");
        var callsAfterFailure = catalog.RadioCalls;
        await radio.NextAsync();
        Check(catalog.RadioCalls == callsAfterFailure, "Automatic navigation ignored failure backoff");
        catalog.Fail = false;
        await radio.RetryRadioAsync();
        Check(radio.Queue.Count == 11 && radio.Autoplay, "Retry after failure did not start a fresh radio");

        var manual = Song("manual"); radio.Enqueue(manual);
        var original = radio.Queue.ToArray();
        catalog.Fail = true;
        await radio.RetryRadioAsync();
        Check(radio.Queue.SequenceEqual(original), "Failed refresh discarded existing recommendations");
        catalog.Fail = false; catalog.Empty = true;
        await radio.RetryRadioAsync();
        Check(radio.Queue.SequenceEqual(original), "Empty refresh discarded existing recommendations");
        catalog.Empty = false;
        await radio.RetryRadioAsync();
        Check(radio.Queue.Count == 12 && radio.Queue[1] == manual && radio.Current == seed,
            "Manual refresh failed with upcoming tracks or moved manual/current tracks");
        Check(!radio.Queue.Skip(2).Any(t => original.Contains(t)), "Refresh kept stale recommended tracks");
        var promoted = radio.Queue[^1]; radio.Enqueue(promoted);
        Check(radio.Queue[2] == promoted && !radio.IsRecommended(promoted), "Manual selection did not take priority over recommendations");
        radio.Results.Add(Song("search-result")); radio.EnqueueResults();
        Check(radio.Queue[3].Id == "search-result", "Search results were added behind recommendations");
        var oldRecommended = radio.Queue.Last();
        radio.ClearQueue(); radio.Enqueue(oldRecommended);
        Check(!radio.IsRecommended(oldRecommended), "Cleared recommendation retained stale classification");
        await radio.RetryRadioAsync();
        Check(radio.Queue.Count > 2, "Manual retry was disabled by explicit queue clear");

        // Ignore cancellation in the fake provider to exercise stale completion protection.
        var pending = new TaskCompletionSource<IReadOnlyList<Track>>();
        var staleCatalog = new QueueCatalog { RadioHandler = (_, _) => pending.Task };
        var stale = Controller(staleCatalog);
        await stale.PlayAsync(seed);
        var oldRequest = stale.RetryRadioAsync();
        Check(stale.RadioLoading, "Radio did not expose loading state");
        var sameRequest = stale.RetryRadioAsync();
        Check(ReferenceEquals(oldRequest, sameRequest) && staleCatalog.RadioCalls == 1, "Double click launched concurrent requests");
        stale.ClearQueue(); staleCatalog.RadioHandler = null;
        var freshRequest = stale.RetryRadioAsync();
        await freshRequest;
        var fresh = stale.Queue.ToArray();
        pending.SetResult([Song("obsolete")]); await oldRequest;
        Check(stale.Queue.SequenceEqual(fresh) && !stale.RadioLoading && staleCatalog.RadioCalls == 2,
            "Cancelled request blocked retry or overwrote the new radio");

        pending = new TaskCompletionSource<IReadOnlyList<Track>>();
        staleCatalog.RadioHandler = (_, _) => pending.Task;
        var disabling = stale.RetryRadioAsync();
        stale.SetAutoplay(false);
        pending.SetResult([Song("disabled")]); await disabling;
        Check(stale.Queue.SequenceEqual(fresh) && !stale.RadioLoading, "Disabled radio still inserted recommendations");

        pending = new TaskCompletionSource<IReadOnlyList<Track>>();
        staleCatalog.RadioHandler = (_, _) => pending.Task;
        var replacing = stale.RetryRadioAsync();
        var selected = Song("selected"); stale.ReplaceQueue([seed, selected]);
        pending.SetResult([Song("replaced")]); await replacing;
        Check(stale.Queue.SequenceEqual([seed, selected]), "Old radio overwrote a replacement queue");

        // Queue changes during the await must be merged, not cause all suggestions to be lost.
        pending = new TaskCompletionSource<IReadOnlyList<Track>>();
        var merging = stale.RetryRadioAsync();
        var inserted = Song("inserted"); stale.PlayNext(inserted);
        pending.SetResult([inserted, Song("new-radio")]); await merging;
        Check(stale.Queue.SequenceEqual([seed, inserted, selected, Song("new-radio")]),
            "Concurrent manual queue edit was lost, duplicated, or discarded all recommendations");

        // Starting a second station must not inherit the first station's pending task.
        pending = new TaskCompletionSource<IReadOnlyList<Track>>();
        var switching = stale.RetryRadioAsync();
        staleCatalog.RadioHandler = null;
        var newSeed = Song("new-seed");
        await stale.StartRadioAsync(newSeed);
        pending.SetResult([Song("wrong-station")]); await switching;
        Check(stale.Current == newSeed && stale.Queue.Count == 11 && !stale.Queue.Any(t => t.Id == "wrong-station"),
            "New station joined an old request or failed to fill");

        var navigationCatalog = new QueueCatalog(); var navigation = Controller(navigationCatalog);
        await navigation.PlayAsync(seed);
        var navigationResult = new TaskCompletionSource<IReadOnlyList<Track>>();
        navigationCatalog.RadioHandler = (_, _) => navigationResult.Task;
        var navigationRadio = navigation.RetryRadioAsync();
        var firstNext = navigation.NextAsync(); var duplicateNext = navigation.NextAsync();
        var navigationNext = Song("navigation-next");
        navigationResult.SetResult([navigationNext, Song("navigation-later")]);
        await Task.WhenAll(navigationRadio, firstNext, duplicateNext);
        Check(navigation.Current == navigationNext, "Concurrent next commands skipped multiple tracks");

        navigation.ClearQueue();
        navigationResult = new TaskCompletionSource<IReadOnlyList<Track>>();
        var interruptedRadio = navigation.RetryRadioAsync();
        var interruptedNext = navigation.NextAsync();
        navigation.ReplaceQueue([navigation.Current!, selected]);
        navigationResult.SetResult([Song("obsolete-next")]);
        await Task.WhenAll(interruptedRadio, interruptedNext);
        Check(navigation.Current == navigationNext && navigation.Upcoming.Single() == selected,
            "Pending next command advanced a replacement queue");

        // Resolve audio asynchronously while moving a queued track.
        var resolving = new TaskCompletionSource<AudioSource>();
        var movingCatalog = new QueueCatalog(); var moving = Controller(movingCatalog);
        var a = Song("a"); var b = Song("b"); var c = Song("c");
        moving.ReplaceQueue([a, b, c]); await moving.PlayAsync(a, 0);
        movingCatalog.ResolveHandler = (_, _) => resolving.Task;
        var playback = moving.PlayAsync(b, 1); moving.EditQueue(b, 1);
        resolving.SetResult(new AudioSource("test", new Dictionary<string, string>())); await playback;
        Check(moving.CurrentQueueIndex == 2 && !moving.Upcoming.Any(), "Playback retained stale queue index after reordering");
        movingCatalog.ResolveHandler = null;
        await moving.PreviousAsync();
        Check(moving.Current == c, "Previous used the wrong index after reordering during resolution");
        var equivalent = c with { Title = "Updated metadata" }; moving.PlayNext(equivalent);
        Check(moving.Queue.Any(t => t.Source == c.Source) && moving.CurrentQueueIndex >= 0, "Play next removed current source with different metadata");

        // Session deserialization can create distinct metadata-array instances.
        var restoreDirectory = Path.Combine(directory, "queue-restore");
        var restoreTrack = seed with { EquivalentVideoIds = ["alternative"] };
        new PlaybackSessionStore(restoreDirectory).Save(new([restoreTrack, a], restoreTrack, 0, 15));
        var restored = new MusicController(new QueueCatalog(), new Player(), new LibraryStore(restoreDirectory), restoreDirectory);
        restored.StopObserving(); await restored.InitializeAsync();
        Check(restored.CurrentQueueIndex == 0 && restored.Upcoming.Single() == a, "Restored metadata arrays broke current-track lookup");

        await CheckQueueInterfaceAsync(view, window);
        foreach (var controller in new[] { radio, stale, navigation, moving, restored }) await controller.FlushPendingSavesAsync();
    }

    private static async Task CheckQueueInterfaceAsync(MainView view, Window window)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Field(string name) => typeof(MainView).GetField(name, flags)!.GetValue(view);
        void Invoke(string name) => typeof(MainView).GetMethod(name, flags)!.Invoke(view, null);
        var music = AppServices.Controller!;
        if (Field("_onboarding") is Border welcome)
        {
            (Field("_artistSearch") as CancellationTokenSource)?.Cancel();
            ((Panel)welcome.Parent!).Children.Remove(welcome);
            typeof(MainView).GetField("_onboarding", flags)!.SetValue(view, null);
        }
        music.SetAutoplay(false);
        var current = new Track("ui-current", "Current song", "Artist", "ui-current", 180);
        var next = new Track("ui-next", "Next song", "Artist", "ui-next", 180);
        var last = new Track("ui-last", "Last song", "Artist", "ui-last", 180);
        music.ReplaceQueue([current, next, last]); await music.PlayAsync(current, 0);
        window.Width = 1260; window.Height = 900;
        Invoke("ShowPlayer"); await Task.Delay(100);
        var detailQueue = (ItemsControl)Field("_detailQueue")!;
        Check(detailQueue.Items.Cast<Track>().SequenceEqual([next, last]), "Player queue included played/current tracks");
        music.EditQueue(next, 0); await Task.Delay(50);
        Check(((TextBlock)Field("_detailNext")!).Text == last.Title && detailQueue.Items.Cast<Track>().SequenceEqual([last]),
            "Player next-song label or queue stayed stale after removal");
        Capture(view, "queue-player-desktop"); Invoke("ClosePlayer");
        foreach (var width in new[] { 360, 390, 430, 1260 })
        {
            window.Width = width; window.Height = 800;
            await Task.Delay(120);
            typeof(MainView).GetField("_page", flags)!.SetValue(view, "queue");
            Invoke("Refresh"); await Task.Delay(200);
            Check(((Control)Field("_mobileQueue")!).IsEffectivelyVisible, "Shared queue page missing at width " + width);
            var upcoming = (ItemsControl)Field("_mobileQueueItems")!;
            Check(upcoming.Items.Count == 1 && upcoming.GetVisualDescendants().OfType<TextBlock>()
                .Any(label => label.Text == last.Title), "Queue shows incorrect upcoming tracks at width " + width);
            Check(((Button)Field("_mobileQueueRetry")!).IsEnabled, "Radio retry unavailable with upcoming tracks");
            Invoke("ShowMobileQueueOptions"); await Task.Delay(40);
            var first = Field("_overlay");
            Invoke("CloseSheet"); Invoke("ShowMobileQueueOptions"); await Task.Delay(40);
            Check(!ReferenceEquals(first, Field("_overlay")), "Radio options did not reopen");
            await Task.Delay(200); Capture(view, "queue-radio-" + width); Invoke("CloseSheet");
        }
        var page = typeof(MainView).GetField("_page", flags)!;
        page.SetValue(view, "home");
        Invoke("Refresh");
        await (Task)typeof(MainView).GetMethod("ToggleDesktopQueueAsync", flags)!.Invoke(view, null)!;
        await Task.Delay(100);
        var side = (Border)Field("_queuePane")!;
        Check(side.IsEffectivelyVisible && ReferenceEquals(side.Child, Field("_mobileQueue")),
            "Desktop dock did not open the shared queue in the side panel");

        page.SetValue(view, "library");
        Invoke("Refresh");
        await Task.Delay(100);
        Check(((Control)Field("_mobileLibrary")!).IsEffectivelyVisible, "Windows did not open the shared library");
        Check((int)typeof(MainView).GetProperty("LibraryColumns", flags)!.GetValue(view)! > 2,
            "Desktop library did not fit multiple columns alongside the queue");

        page.SetValue(view, "explore");
        Invoke("Refresh");
        var search = (TextBox)Field("_search")!;
        search.Text = "desktop search";
        await (Task)typeof(MainView).GetMethod("SearchAsync", flags)!.Invoke(view, null)!;
        await Task.Delay(100);
        Check(((ItemsControl)Field("_mobileSearchItems")!).IsEffectivelyVisible,
            "Windows did not show results in the shared search page");

        music.StopObserving(); await music.FlushPendingSavesAsync();
    }

    private sealed class QueueCatalog : IMusicCatalog
    {
        public bool Fail { get; set; }
        public bool Empty { get; set; }
        public int RadioCalls { get; private set; }
        public Func<Track, CancellationToken, Task<IReadOnlyList<Track>>>? RadioHandler { get; set; }
        public Func<Track, CancellationToken, Task<AudioSource>>? ResolveHandler { get; set; }
        public Task<IReadOnlyList<Track>> RadioAsync(Track seed, CancellationToken token)
        {
            RadioCalls++;
            if (RadioHandler is not null) return RadioHandler(seed, token);
            if (Fail) throw new HttpRequestException("Simulated radio outage");
            return Task.FromResult<IReadOnlyList<Track>>(Empty ? [] : Enumerable.Range(0, 10)
                .Select(i => new Track($"batch{RadioCalls}-{i}", $"Melody batch{RadioCalls}-{i}", "Artist " + i,
                    $"https://example.org/batch{RadioCalls}-{i}", 180)).ToArray());
        }
        public Task<IReadOnlyList<Track>> SearchAsync(string query, CancellationToken token)
        {
            if (Fail) throw new HttpRequestException("Simulated fallback outage");
            return Task.FromResult<IReadOnlyList<Track>>([]);
        }
        public Task<AudioSource> ResolveAsync(Track track, CancellationToken token) => ResolveHandler?.Invoke(track, token)
            ?? Task.FromResult(new AudioSource("test", new Dictionary<string, string>()));
    }
}
