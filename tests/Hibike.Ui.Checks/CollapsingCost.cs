using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Headless;
using Hibike.App;
using Hibike.App.Views;
using Hibike.Core.Models;

internal static partial class Program
{
    // Per-frame cost of scrolling Início with the collapsing app bar versus plain offset changes.
    // Synthetic and headless: compares the two paths on the same machine, not device frame times.
    private static async Task MeasureCollapsingCostAsync(Window window)
    {
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        var music = AppServices.Controller!;
        music.StopObserving();
        await music.InitializeAsync();
        var fixture = Enumerable.Range(0, 60).Select(i => new Track("cost-" + i, "Cost sample " + i, "Artist " + i % 9,
            "cost-source-" + i, 200, Album: "Album " + i % 5)).ToArray();
        foreach (var track in fixture.Take(30)) music.Favorites.Add(track);
        foreach (var track in fixture.Skip(10).Take(30)) music.History.Add(track);
        typeof(MusicController).GetField("<HomeRecommendations>k__BackingField", hidden)!.SetValue(music, fixture.Skip(30).ToArray());
        var view = (MainView)Activator.CreateInstance(typeof(MainView), hidden, null, [music, true], null)!;
        window.Width = 390; window.Height = 844; window.Content = view;
        await Task.Delay(300);
        var home = (ScrollViewer)typeof(MainView).GetField("_homeScroll", hidden)!.GetValue(view)!;
        var presenter = home.GetVisualDescendants().OfType<ScrollContentPresenter>().First();
        view.UpdateLayout();
        var recognizer = presenter.GestureRecognizers.OfType<Avalonia.Input.GestureRecognizers.ScrollGestureRecognizer>().FirstOrDefault();
        Console.WriteLine($"Scroll start distance: {recognizer?.ScrollStartDistance} DIP (inertia {recognizer?.IsScrollInertiaEnabled})");

        double Run(bool collapsing, bool render = false)
        {
            home.Offset = default;
            typeof(MainView).GetMethod("RevealMobileTopBars", hidden)!.Invoke(view, null);
            view.UpdateLayout();
            var frames = new List<double>();
            for (var step = 0; step < 120; step++)
            {
                var delta = step < 60 ? 6 : -6;
                var start = Stopwatch.GetTimestamp();
                if (collapsing) presenter.RaiseEvent(new ScrollGestureEventArgs(88, new Vector(0, delta)));
                else home.Offset = new Vector(0, Math.Max(0, home.Offset.Y + delta));
                if (render) window.CaptureRenderedFrame()?.Dispose(); else view.UpdateLayout();
                frames.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            }
            presenter.RaiseEvent(new ScrollGestureEndedEventArgs(88));
            frames.Sort();
            return frames[frames.Count / 2];
        }
        Run(false); Run(true); // warm-up
        var plain = Enumerable.Range(0, 5).Select(_ => Run(false)).Order().ElementAt(2);
        var collapsing = Enumerable.Range(0, 5).Select(_ => Run(true)).Order().ElementAt(2);
        Console.WriteLine($"Median frame: plain scroll {plain:0.000} ms | scroll with collapsing bar {collapsing:0.000} ms");
        Run(false, true); Run(true, true);
        var plainRender = Enumerable.Range(0, 3).Select(_ => Run(false, true)).Order().ElementAt(1);
        var collapsingRender = Enumerable.Range(0, 3).Select(_ => Run(true, true)).Order().ElementAt(1);
        Console.WriteLine($"Median frame incl. render: plain scroll {plainRender:0.00} ms | scroll with collapsing bar {collapsingRender:0.00} ms");
    }
}
