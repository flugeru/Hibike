using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Hibike.App;
using Hibike.App.Views;
using Hibike.Core.Models;

internal static partial class Program
{
    // Captures every main Android screen with representative offline data, for visual review of
    // the real mobile layout (the default checks render the desktop layout at phone widths).
    private static async Task CaptureAndroidScreensAsync(Window window)
    {
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        var music = AppServices.Controller!;
        music.StopObserving(); music.SetAutoplay(false);
        await music.InitializeAsync();
        var fixture = Enumerable.Range(0, 40).Select(i => new Track("screen-" + i,
            i % 5 == 0 ? "A considerably longer song title number " + i : "Song title " + i, "Artist " + i % 7,
            "offline-screen-" + i, 150 + i * 7, Album: "Album " + i % 4)).ToArray();
        foreach (var track in fixture.Take(18)) music.Favorites.Add(track);
        foreach (var track in fixture.Skip(5).Take(14)) music.History.Add(track);
        typeof(MusicController).GetField("<HomeRecommendations>k__BackingField", hidden)!.SetValue(music, fixture.Skip(18).Take(16).ToArray());
        music.ReplaceQueue(fixture.Take(12));
        await music.PlayAsync(fixture[0], 0);

        foreach (var (width, height) in new[] { (360d, 780d), (412d, 915d) })
        {
            var view = (MainView)Activator.CreateInstance(typeof(MainView), hidden, null, [music, true], null)!;
            window.Width = width; window.Height = height; window.Content = view;
            await Task.Delay(250);
            void Navigate(string route) => view.GetVisualDescendants().OfType<Button>().Single(button =>
                button.IsEffectivelyVisible && button.Tag is ValueTuple<string, string, string, bool> tab && tab.Item1 == route)
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            foreach (var page in new[] { "home", "explore", "library", "downloads", "queue" })
            {
                Navigate(page);
                await Task.Delay(250);
                Capture(view, $"android-{page}-{(int)width}");
            }
            typeof(MainView).GetMethod("ShowPlayer", hidden)!.Invoke(view, null);
            await Task.Delay(400);
            Capture(view, $"android-player-{(int)width}");
            AppServices.BackHandler!();
            await Task.Delay(300);
        }
        Console.WriteLine("Android screens captured beside the executable (android-*.png).");
    }
}
