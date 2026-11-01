using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hibiki.App;
using Hibiki.App.Views;
using Hibiki.Core.Models;
using Hibiki.Core.Services;

// Native Windows check. No network, real audio, or changes to the user's library.
internal static class Program
{
    [STAThread]
    public static int Main()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hibiki-ui-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        AppServices.Configure(new Catalog(), new Player(), directory);
        var result = 1;
        var builder = AppBuilder.Configure<Hibiki.App.App>().UsePlatformDetect().WithInterFont();
        builder.AfterSetup(_ => Dispatcher.UIThread.Post(async () =>
        {
            var lifetime = (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
            try
            {
                var window = lifetime.MainWindow!; var view = (MainView)window.Content!;
                await AppServices.Controller!.SearchAsync("test");
                foreach (var width in new[] { 360, 390, 430, 1260, 1920 })
                {
                    window.Width = width; window.Height = 820;
                    await Task.Delay(150);
                    Check(view.GetVisualDescendants().OfType<TextBlock>().All(t => t.FontSize >= 12), "Text below 12");
                    foreach (var button in view.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible))
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
                    var slider = view.GetVisualDescendants().OfType<Slider>().Single();
                    Check(slider.TranslatePoint(default, view)!.Value.Y + slider.Bounds.Height <= view.Bounds.Height, "Player slider below viewport");
                    if (width >= 1260)
                    {
                        var artwork = view.GetVisualDescendants().OfType<Border>().Single(b => b.GetType().Name == "Artwork" && b.Bounds.Width > 100);
                        Check(artwork.Bounds.Width <= 440, "Desktop artwork exceeds size cap");
                        var queueHeader = view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "A seguir");
                        Check(Math.Abs(artwork.TranslatePoint(default, view)!.Value.Y - queueHeader.TranslatePoint(default, view)!.Value.Y) < 8, "Artwork and queue tops diverge");
                    }
                    if (width == 1260)
                    {
                        var marquee = view.GetVisualDescendants().OfType<Canvas>().Single(c => c.GetType().Name == "ScrollingTitle");
                        var movingTitle = (TextBlock)marquee.Children[0];
                        await Task.Delay(2300);
                        Check(((Avalonia.Media.TranslateTransform)movingTitle.RenderTransform!).X < 0, "Overflow title did not scroll");
                        window.Width = 390; await Task.Delay(300); Capture(view, "player-resized-mobile");
                        window.Width = width; await Task.Delay(300); Capture(view, "player-resized-desktop");
                    }
                    Check(AppServices.BackHandler!(), "Player cannot close");
                    await Task.Delay(300);
                    var more = view.GetVisualDescendants().OfType<Button>().First(b => Avalonia.Automation.AutomationProperties.GetName(b)?.StartsWith("Opções de ") == true);
                    more.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); await Task.Delay(300);
                    Capture(view, $"dialog-{width}"); Check(AppServices.BackHandler!(), "Dialog cannot close");
                }
                Console.WriteLine("UI checks passed: 360/390/430, typography, touch targets, virtualization, player.");
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
            Task.FromResult<IReadOnlyList<Track>>(Enumerable.Range(0, 1000).Select(i => new Track(i.ToString(), "Artist - Clair Obscur: Expedition 33 (Original Soundtrack) — A deliberately long title for overflow and responsive layout checks (Official Video)", "Artist - Topic", "test", 267.8)).ToArray());
        public Task<AudioSource> ResolveAsync(Track track, CancellationToken cancellationToken) => Task.FromResult(new AudioSource("test", new Dictionary<string, string>()));
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
