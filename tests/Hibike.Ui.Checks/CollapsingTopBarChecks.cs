using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Hibike.App;
using Hibike.App.Views;

internal static partial class Program
{
    private static async Task CheckCollapsingTopBarsAsync(Window window)
    {
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        var music = AppServices.Controller!;
        var previous = window.Content;
        var previousMotion = music.ReduceMotion;
        var view = (MainView)Activator.CreateInstance(typeof(MainView), hidden, null, [music, true], null)!;
        window.Width = 390;
        window.Height = 760;
        window.Content = view;

        T Field<T>(string name) => (T)typeof(MainView).GetField(name, hidden)!.GetValue(view)!;
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        void Navigate(string route) => Click(view.GetVisualDescendants().OfType<Button>().Single(button =>
            button.IsEffectivelyVisible && button.Tag is ValueTuple<string, string, string, bool> tab && tab.Item1 == route));
        static ScrollContentPresenter Presenter(ScrollViewer scroll) =>
            scroll.GetVisualDescendants().OfType<ScrollContentPresenter>().First();
        static void Swipe(ScrollViewer scroll, double x, double y) =>
            Presenter(scroll).RaiseEvent(new ScrollGestureEventArgs(71, new Vector(x, y)));
        static void EndSwipe(ScrollViewer scroll) => Presenter(scroll).RaiseEvent(new ScrollGestureEndedEventArgs(71));
        static Control[] Bars(ScrollViewer scroll) => scroll.GetVisualAncestors().OfType<Grid>()
            .SelectMany(grid => grid.Children).Where(child => child.GetType().Name == "CollapsibleTopBar" && child.IsEffectivelyVisible).ToArray();

        try
        {
            await Task.Delay(160);
            music.SetReduceMotion(false);
            var home = Field<ScrollViewer>("_homeScroll");
            home.Content = new Border { Height = 4000 };
            view.UpdateLayout();
            var header = Bars(home).Single();
            var expandedHeight = header.Bounds.Height;
            var expandedViewport = home.Viewport.Height;
            Check(expandedHeight > 0, "Home toolbar failed to measure");
            Swipe(home, 0, 24);
            view.UpdateLayout();
            Check(Math.Abs(header.Bounds.Height - (expandedHeight - 24)) < 1, "Toolbar did not follow the drag distance");
            EndSwipe(home);
            await Task.Delay(340);
            Check(header.Bounds.Height < 1 && !header.IsHitTestVisible && home.Viewport.Height >= expandedViewport + expandedHeight - 1,
                "Collapsed home toolbar did not release its layout space");
            Swipe(home, 0, 300);
            var offsetBeforeReveal = home.Offset.Y;
            Swipe(home, 0, -16);
            view.UpdateLayout();
            Check(Math.Abs(header.Bounds.Height - 16) < 1, "Reverse drag jumped straight to the expanded state");
            Check(Math.Abs(home.Offset.Y - offsetBeforeReveal) < 1, "Reverse drag moved both the toolbar and the list");
            EndSwipe(home);
            await Task.Delay(340);
            Check(header.Bounds.Height == expandedHeight && home.Offset.Y > 0, "Reverse scrolling did not reveal the toolbar away from the top");

            music.SetReduceMotion(true);
            Navigate("library");
            for (var i = 0; i < 20; i++) music.CreatePlaylist("Scroll check " + i);
            Click(Field<List<Button>>("_mobileLibraryTabs")[1]);
            await Task.Delay(100);
            var library = Field<ScrollViewer>("_mobileLibraryScroll");
            var libraryBar = Bars(library).Single();
            var libraryHeight = libraryBar.Bounds.Height;
            var horizontal = new ScrollViewer { Content = new Border { Width = 800 },
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden };
            var libraryContent = library.Content;
            library.Content = new StackPanel { Children = { horizontal, new Border { Height = 3000 } } };
            view.UpdateLayout();
            Swipe(horizontal, 80, 5);
            Swipe(horizontal, 0, 45);
            view.UpdateLayout();
            Check(libraryBar.Bounds.Height == libraryHeight, "Nested horizontal carousel collapsed the page toolbar");
            library.Content = libraryContent;
            view.UpdateLayout();
            Swipe(library, 0, 500);
            view.UpdateLayout();
            Check(libraryBar.Bounds.Height < 1, "Library tabs and filters did not collapse");
            Click(Field<List<Button>>("_nav").First(button => button.Tag is ValueTuple<string, string, string, bool> tab
                && tab.Item1 == "library" && button.IsEffectivelyVisible));
            view.UpdateLayout();
            Check(libraryBar.Bounds.Height == libraryHeight, "Selecting the current tab did not restore its controls");

            Navigate("downloads");
            view.UpdateLayout();
            var downloads = Field<Grid>("_downloadLibrary").GetVisualDescendants().OfType<ScrollViewer>()
                .Single(scroll => ReferenceEquals(scroll.Content, Field<ItemsControl>("_downloadItems")));
            Field<ScrollViewer>("_downloadEmpty").IsVisible = false;
            downloads.Content = new Border { Height = 4000 };
            view.UpdateLayout();
            var downloadBars = Bars(downloads);
            Check(downloadBars.Length == 3, "Downloads did not link the app bar, tabs, and filters");
            Swipe(downloads, 0, 500);
            view.UpdateLayout();
            Check(downloadBars.All(bar => bar.Bounds.Height < 1), "Downloads left fixed controls above its list");
            Swipe(downloads, 0, -40);
            view.UpdateLayout();
            Check(downloadBars.Sum(bar => bar.Bounds.Height) is > 39 and < 41
                && downloadBars.Count(bar => bar.Bounds.Height > 0) == 1, "Download controls did not slide back as a single stack");
            EndSwipe(downloads);
            await Task.Delay(200);
            Check(downloadBars.All(bar => bar.Bounds.Height > 0), "Downloads did not restore its toolbar groups");

            Click(Field<Button>("_transfersTab"));
            var transfers = Field<ScrollViewer>("_transferScroll");
            Field<ScrollViewer>("_transferEmpty").IsVisible = false;
            transfers.IsVisible = true;
            transfers.Content = new Border { Height = 4000 };
            view.UpdateLayout();
            var transferBars = Bars(transfers);
            Swipe(transfers, 0, 500);
            view.UpdateLayout();
            Check(transferBars.Length == 3 && transferBars.All(bar => bar.Bounds.Height < 1),
                "Transfer tabs or queue header did not collapse");

            Navigate("explore");
            var search = Field<ScrollViewer>("_mobileSearchScroll");
            search.Content = new Border { Height = 4000 };
            search.IsVisible = true;
            Field<ContentControl>("_mobileSearchState").IsVisible = false;
            view.UpdateLayout();
            var searchBars = Bars(search);
            Swipe(search, 0, 500);
            view.UpdateLayout();
            Check(searchBars.Length == 2 && searchBars.All(bar => bar.Bounds.Height < 1),
                "Search left its field or filters fixed above results");

            Navigate("queue");
            var queue = Field<ScrollViewer>("_mobileQueueScroll");
            queue.Content = new Border { Height = 4000 };
            queue.IsVisible = true;
            Field<ContentControl>("_mobileQueueEmpty").IsVisible = false;
            view.UpdateLayout();
            var queueBars = Bars(queue);
            Swipe(queue, 0, 500);
            view.UpdateLayout();
            Check(queueBars.Length == 1 && queueBars[0].Bounds.Height < 1, "Queue toolbar did not collapse");

            typeof(MainView).GetMethod("OpenCollection", hidden)!.Invoke(view,
                ["Collection check", Enumerable.Range(1, 20).Select(i => new Hibike.Core.Models.Track(
                    i.ToString(), "Track " + i, "Artist", "source-" + i)).ToArray(),
                    "Playlist", null, null, null, false, false, null]);
            await Task.Delay(100);
            var collection = Field<Border>("_collectionPage").GetVisualDescendants().OfType<ScrollViewer>().Single();
            Swipe(collection, 0, 500);
            view.UpdateLayout();
            Check(Bars(collection).Length == 2 && Bars(collection).All(bar => bar.Bounds.Height < 1),
                "Collection toolbar and artwork header did not collapse");

            typeof(MainView).GetMethod("OpenPreferencesPage", hidden)!.Invoke(view,
                ["settings", new Control[] { new Border { Height = 4000 } }]);
            view.UpdateLayout();
            var preferences = Field<ScrollViewer>("_preferencesPage");
            Swipe(preferences, 0, 500);
            view.UpdateLayout();
            Check(header.Bounds.Height < 1, "Settings did not use the shared toolbar");
            Swipe(preferences, 0, -500);
            view.UpdateLayout();
            Check(header.Bounds.Height == expandedHeight, "Settings did not restore its toolbar");

            Navigate("home");
            view.UpdateLayout();
            Check(header.Bounds.Height == expandedHeight, "Leaving a collapsed screen hid the next page toolbar");
        }
        finally
        {
            window.Content = previous;
            music.SetReduceMotion(previousMotion);
        }
    }
}
