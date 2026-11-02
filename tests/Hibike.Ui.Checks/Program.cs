using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hibike.App;
using Hibike.App.Views;
using Hibike.Core.Models;
using Hibike.Core.Services;

// Native Windows check. No network, real audio, or changes to the user's library.
internal static class Program
{
    [STAThread]
    public static int Main()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hibike-ui-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        AppServices.Configure(new Catalog(), new Player(), directory);
        var result = 1;
        var builder = AppBuilder.Configure<Hibike.App.App>().UsePlatformDetect().WithInterFont();
        builder.AfterSetup(_ => Dispatcher.UIThread.Post(async () =>
        {
            var lifetime = (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
            try
            {
                var window = lifetime.MainWindow!; var view = (MainView)window.Content!;
                await Task.Delay(200);
                Check(view.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Ouvir de novo" && t.IsEffectivelyVisible), "Home listening history shelf missing");
                AppServices.Controller!.History.Add(new("home-check", "Faixa recente", "Artista", "test"));
                AppServices.Controller.SetStatus("Home check");
                foreach (var homeWidth in new[] { 360, 1260 })
                {
                    window.Width = homeWidth; await Task.Delay(150); Capture(view, $"home-{homeWidth}");
                    var card = view.GetVisualDescendants().OfType<Button>().Single(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Faixa recente — Artista");
                    Check(card.Bounds.Width >= 160 && card.IsEffectivelyVisible, "Home history cover card missing");
                    foreach (var button in view.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible && !b.GetVisualAncestors().OfType<Slider>().Any()))
                        Check(button.Bounds.Height >= 48, "Home touch target below 48");
                }
                typeof(MainView).GetField("_page", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(view, "explore");
                var imageUrl = typeof(MainView).Assembly.GetType("Hibike.App.Views.Artwork")!.GetMethod("PlayerImageUrl")!;
                Check((string)imageUrl.Invoke(null, ["https://i.ytimg.com/vi/abc/hqdefault.jpg"])! == "https://i.ytimg.com/vi/abc/maxresdefault.jpg", "Player did not request high-resolution YouTube artwork");
                Check((string)imageUrl.Invoke(null, ["https://example.org/cover.jpg"])! == "https://example.org/cover.jpg", "Unrelated artwork URL rewritten");
                await AppServices.Controller!.SearchAsync("test");
                foreach (var width in new[] { 360, 390, 430, 1260, 1920 })
                {
                    window.Width = width; window.Height = 820;
                    await Task.Delay(150);
                    Check(view.GetVisualDescendants().OfType<TextBlock>().All(t => t.FontSize >= 12), "Text below 12");
                    foreach (var button in view.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible && !b.GetVisualAncestors().OfType<Slider>().Any()))
                        Check(button.Bounds.Height >= 48, $"Touch target below 48: {button.Content} {button.Classes} {button.Bounds}");
                    var list = view.GetVisualDescendants().OfType<ItemsControl>().First(c => c.ItemsSource is System.Collections.ObjectModel.ObservableCollection<Track>);
                    Check(list.GetVisualDescendants().OfType<Button>().Count() < 100, "List not virtualized");
                    Capture(view, $"search-{width}");
                    var search = view.GetVisualDescendants().OfType<TextBox>().First();
                    search.Text = "test"; await Task.Delay(30);
                    var clear = ((StackPanel)search.InnerRightContent!).Children[0];
                    Check(clear.IsVisible, "Search clear button missing");
                    search.Text = ""; await Task.Delay(30);
                    Check(!clear.IsVisible, "Empty search has clear button");
                    await AppServices.Controller.PlayAsync(AppServices.Controller.Results[0]);
                    await Task.Delay(100); Capture(view, $"mini-{width}");
                    AppServices.OpenPlayerView!(); await Task.Delay(300);
                    Capture(view, $"player-{width}");
                    Check(view.GetVisualDescendants().OfType<Slider>().Any(s => s.Bounds.Width > 0 && s.Bounds.Width <= width), "Player slider clipped");
                    var slider = view.GetVisualDescendants().OfType<Slider>().Single(s => s.IsEffectivelyVisible && Avalonia.Automation.AutomationProperties.GetName(s) != "Volume");
                    Check(slider.TranslatePoint(default, view)!.Value.Y + slider.Bounds.Height <= view.Bounds.Height, "Player slider below viewport");
                    if (width >= 1260)
                    {
                        var artwork = view.GetVisualDescendants().OfType<Border>().Single(b => b.GetType().Name == "Artwork" && b.Bounds.Width > 100 && b.IsEffectivelyVisible);
                        Check(artwork.Bounds.Width <= 500, "Desktop artwork exceeds size cap");
                        var slot = (Grid)artwork.Parent!;
                        Check(Math.Abs(artwork.Bounds.Center.Y - slot.Bounds.Height / 2) < 1, "Artwork not vertically centered");
                        Check(Math.Abs(artwork.Bounds.Center.X - slot.Bounds.Width / 2) < 1, "Artwork not horizontally centered");
                    }
                    if (width == 1260)
                    {
                        var marquee = view.GetVisualDescendants().OfType<Canvas>().Single(c => c.GetType().Name == "ScrollingTitle");
                        var movingTitle = (TextBlock)marquee.Children[0];
                        await Task.Delay(2300);
                        Check(((Avalonia.Media.TranslateTransform)movingTitle.RenderTransform!).X < 0, "Overflow title did not scroll");
                        window.Width = 390; await Task.Delay(300); Capture(view, "player-resized-mobile");
                        window.Width = width; await Task.Delay(300); Capture(view, "player-resized-desktop");
                        var artwork = view.GetVisualDescendants().OfType<Border>().Single(b => b.GetType().Name == "Artwork" && b.Bounds.Width > 100 && b.IsEffectivelyVisible);
                        window.Width = width + 1; await Task.Delay(300);
                        Check(Math.Abs(artwork.Bounds.Width - artwork.Bounds.Height) < 1, "Player artwork not square");
                        Check(artwork.GetVisualDescendants().OfType<Image>().Single().Stretch == Avalonia.Media.Stretch.UniformToFill, "Player artwork not filling square");
                        Capture(view, "player-widescreen-artwork");
                        var longPlay = view.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("largePlay"));
                        var longControls = (Grid)longPlay.Parent!;
                        var longX = longControls.TranslatePoint(default, view)!.Value.X;
                        Check(AppServices.BackHandler!(), "Long-title player cannot close");
                        await AppServices.Controller.PlayAsync(AppServices.Controller.Results[1]);
                        AppServices.OpenPlayerView!(); await Task.Delay(300);
                        var shortPlay = view.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("largePlay"));
                        var shortControls = (Grid)shortPlay.Parent!;
                        var transport = (Grid)shortControls.Parent!;
                        Check(Math.Abs(shortControls.Bounds.Center.X - transport.Bounds.Width / 2) < 3, "Short-title controls not centered within pixel rounding");
                        Check(longX > shortControls.TranslatePoint(default, view)!.Value.X + 20, "Long title did not push controls right");
                        Capture(view, "player-short-title");
                        var volumeButton = view.GetVisualDescendants().OfType<Button>().Single(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Ajustar volume");
                        var volumeSlider = view.GetVisualDescendants().OfType<Slider>().Single(s => Avalonia.Automation.AutomationProperties.GetName(s) == "Volume");
                        volumeSlider.Value = .35;
                        Check(Math.Abs(AppServices.Controller.Player.Volume - .35) < .001, "Volume slider not connected to player");
                        volumeButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                        await Task.Delay(150); Check(volumeSlider.IsVisible, "Inline volume did not open");
                        Check(volumeButton.GetVisualAncestors().Contains(shortControls), "Volume outside transport controls");
                        Capture(view, "player-inline-volume");
                        volumeButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                        var info = view.GetVisualDescendants().OfType<Button>().Single(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Informações do áudio");
                        info.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); await Task.Delay(150);
                        Check(view.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "160 kb/s"), "Audio bitrate missing from details");
                        Capture(view, "audio-info"); Check(AppServices.BackHandler!(), "Audio details cannot close");
                    }
                    Check(AppServices.BackHandler!(), "Player cannot close");
                    await Task.Delay(300);
                    var more = view.GetVisualDescendants().OfType<Button>().First(b => Avalonia.Automation.AutomationProperties.GetName(b)?.StartsWith("Opções de ") == true);
                    more.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); await Task.Delay(300);
                    Capture(view, $"dialog-{width}"); Check(AppServices.BackHandler!(), "Dialog cannot close");
                }
                var music = AppServices.Controller!;
                await music.ToggleFavoriteAsync(music.Results[0]);
                await music.ToggleFavoriteAsync(new("local-filter", "Local favorite", "Local artist", "test", IsLocal: true));
                foreach (var width in new[] { 360, 1260 })
                {
                    window.Width = width;
                    var libraryNav = view.GetVisualDescendants().OfType<Button>().First(b => b.IsEffectivelyVisible && b.Tag is ValueTuple<string, string, string, bool> tag && tag.Item1 == "library");
                    libraryNav.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); await Task.Delay(150);
                    var filter = view.GetVisualDescendants().OfType<TextBox>().Single(t => t.PlaceholderText == "Filtrar título ou artista");
                    filter.Text = "Local favorite"; await Task.Delay(100);
                    var filtered = view.GetVisualDescendants().OfType<ItemsControl>().Single(c => c.IsEffectivelyVisible);
                    Check(filtered.ItemsSource!.Cast<Track>().Single().Id == "local-filter", "Library filter did not isolate local favorite");
                    Capture(view, $"library-{width}"); filter.Text = "";
                    var queueNav = view.GetVisualDescendants().OfType<Button>().First(b => b.IsEffectivelyVisible && b.Tag is ValueTuple<string, string, string, bool> tag && tag.Item1 == "queue");
                    queueNav.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); await Task.Delay(150); Capture(view, $"queue-{width}");
                }
                var extra = new Track("queue-edit", "Queue edit", "Artist", "test"); music.Enqueue(extra);
                music.EditQueue(extra, -1); Check(music.Queue.IndexOf(extra) == music.Queue.Count - 2, "Queue move failed");
                music.EditQueue(extra, 0); Check(!music.Queue.Contains(extra), "Queue remove failed");
                var current = music.Current!;
                var nextA = extra with { Id = "next-a", Source = "next-a" };
                var nextB = extra with { Id = "next-b", Source = "next-b" };
                music.ReplaceQueue([current, nextA, nextB]);
                music.EditQueue(nextA, -1); Check(music.Queue[0] == current && music.Queue[1] == nextA, "Upcoming track moved before current");
                music.ReorderQueue(true); Check(music.Queue[0] == current, "Shuffle displaced current track");
                typeof(MainView).GetMethod("OpenCollection", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(view, ["Playlist de teste", new Track[] { nextA, nextB }]);
                await Task.Delay(150); Capture(view, "collection-1260");
                Check(view.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Playlist de teste" && t.IsEffectivelyVisible), "Collection page missing");
                Check(AppServices.BackHandler!(), "Collection cannot return home");
                music.SaveSession();
                var restored = new MusicController(new Catalog(), new Player(), new LibraryStore(directory), directory);
                await restored.InitializeAsync();
                Check(restored.Current == AppServices.Controller.Current && restored.Queue.SequenceEqual(AppServices.Controller.Queue), "Playback session not restored");
                Check(!restored.Player.IsPlaying, "Restored session autoplayed");
                restored.ClearQueue(); Check(restored.Queue.Count == 1 && restored.Queue[0] == restored.Current, "Clear queue lost current track");
                restored.StopObserving();
                Console.WriteLine("UI checks passed: 360/390/430, typography, touch targets, virtualization, player, session restore and clear queue.");
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "ui-check-result.txt"), "PASS: 360/390/430, typography, touch targets, virtualization, player.");
                result = 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "ui-check-result.txt"), error.ToString()); }
            finally { lifetime.Shutdown(result); }
        }));
        builder.StartWithClassicDesktopLifetime([]);
        Directory.Delete(directory, true);
        return result;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Capture(Control control, string name)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)control.Bounds.Width, (int)control.Bounds.Height));
        bitmap.Render(control); bitmap.Save(Path.Combine(AppContext.BaseDirectory, name + ".png"));
    }
    private sealed class Catalog : IMusicCatalog
    {
        public Task<IReadOnlyList<Track>> SearchAsync(string query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Track>>(Enumerable.Range(0, 1000).Select(i => new Track(i.ToString(), i == 1 ? "Artist - Short title" : "Artist - Clair Obscur: Expedition 33 (Original Soundtrack) — A deliberately long title for overflow and responsive layout checks (Official Video)", "Artist - Topic", "test", 267.8)).ToArray());
        public Task<AudioSource> ResolveAsync(Track track, CancellationToken cancellationToken) => Task.FromResult(new AudioSource("test", new Dictionary<string, string>(), BitrateKbps: 160, Codec: "opus", SampleRateHz: 48000, Channels: 2, Format: "251", Container: "webm"));
    }
    private sealed class Player : IAudioPlayer
    {
        public bool IsPlaying { get; private set; }
        public double Position => 0;
        public double Duration => 267.7;
        public double Volume { get; set; }
        public event EventHandler? Ended { add { } remove { } }
        public event EventHandler<string>? Failed { add { } remove { } }
        public Task PlayAsync(AudioSource source, CancellationToken cancellationToken) { IsPlaying = true; return Task.CompletedTask; }
        public void Pause() => IsPlaying = false;
        public void Resume() => IsPlaying = true;
        public void Seek(double seconds) { }
        public void Dispose() { }
    }
}
