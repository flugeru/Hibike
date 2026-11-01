using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Automation;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hibiki.App;
using Hibiki.App.Views;
using Hibiki.Core.Models;
using Hibiki.Core.Services;

var directory = Path.Combine(Path.GetTempPath(), "hibiki-ui-smoke-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
var audio = new FakePlayer();
AppServices.Configure(new FakeCatalog(), audio, directory);
Console.WriteLine("Starting the Avalonia headless session.");
await using var session = HeadlessUnitTestSession.StartNew(typeof(SmokeAppBuilder));
await session.Dispatch(async () =>
{
    Console.WriteLine("Building the Hibiki views.");
    var controller = AppServices.CreateController();
    Directory.CreateDirectory("artifacts/ui");
    foreach (var width in new[] { 360d, 430d })
    {
        var view = new MainView(controller);
        var window = new Window { Width = width, Height = 780, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        if (view.Bounds.Width < width - 1 || view.Bounds.Height <= 0)
            throw new InvalidOperationException($"Main view has invalid bounds at {width}dp: {view.Bounds}.");
        var search = view.GetVisualDescendants().OfType<TextBox>().First();
        search.Focus(); window.KeyTextInput("Song");
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Dispatcher.UIThread.RunJobs();
        if (controller.Results.Count != 15)
            throw new InvalidOperationException("Search did not return the expected tracks.");
        var list = view.GetVisualDescendants().OfType<ListBox>().First(x => x.Classes.Contains("music-list"));
        if (list.Items.Count != 15) throw new InvalidOperationException("Search results did not reach the list.");
        await controller.PlayAsync(controller.Results[0]);
        Dispatcher.UIThread.RunJobs();
        await Task.Delay(200);
        using (var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Could not render search."))
            frame.Save($"artifacts/ui/search-{width:0}.png", new PngBitmapEncoderOptions());
        var toggle = view.GetVisualDescendants().OfType<Button>()
            .First(x => AutomationProperties.GetName(x) == "Reproduzir, pausar ou cancelar");
        toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (audio.IsPlaying) throw new InvalidOperationException("Pause did not reach the player.");
        toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (!audio.IsPlaying) throw new InvalidOperationException("Resume did not reach the player.");
        var open = view.GetVisualDescendants().OfType<Button>()
            .First(x => AutomationProperties.GetName(x) == "Abrir player");
        open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        var slider = view.GetVisualDescendants().OfType<Slider>().FirstOrDefault()
            ?? throw new InvalidOperationException("Player did not open its seek control.");
        if (!view.GetVisualDescendants().OfType<TextBlock>().Any(x => x.Text == "4:28"))
            throw new InvalidOperationException("Player duration does not match the track metadata.");
        await Task.Delay(200);
        using (var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Could not render player."))
            frame.Save($"artifacts/ui/player-{width:0}.png", new PngBitmapEncoderOptions());
        var thumb = slider.GetVisualDescendants().OfType<Thumb>().First(x => x.Name == "PlayerThumb");
        var point = thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("Seek thumb is not attached to the window.");
        var destination = new Point(point.X + slider.Bounds.Width / 3, point.Y);
        audio.LastSeek = -1;
        window.MouseDown(point, MouseButton.Left);
        window.MouseMove(destination, RawInputModifiers.LeftMouseButton);
        window.MouseUp(destination, MouseButton.Left);
        if (audio.LastSeek <= 0) throw new InvalidOperationException("Dragging the seek control did not seek playback.");
        if (AppServices.BackHandler?.Invoke() != true)
            throw new InvalidOperationException("Player did not close through the existing back handler.");
        var clear = (Button)search.InnerRightContent!;
        clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (!string.IsNullOrEmpty(search.Text)) throw new InvalidOperationException("Search clear button did not clear the text.");
        window.Close();
    }
    Console.WriteLine("Search, playback, player and return passed at 360dp and 430dp.");
    return 0;
}, CancellationToken.None);
Directory.Delete(directory, true);

public static class SmokeAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

internal sealed class FakeCatalog : IMusicCatalog
{
    public Task<IReadOnlyList<Track>> SearchAsync(string query, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Track>>(Enumerable.Range(0, 15)
            .Select(i => new Track(i.ToString(), $"Artist - Song {i} (Official Video)", "Artist - Topic",
                $"https://example.org/{i}", 267.5)).ToArray());
    public Task<AudioSource> ResolveAsync(Track track, CancellationToken cancellationToken) =>
        Task.FromResult(new AudioSource(track.Source, new Dictionary<string, string>()));
}

internal sealed class FakePlayer : IAudioPlayer
{
    public bool IsPlaying { get; private set; }
    public double LastSeek { get; set; }
    public double Position => Math.Max(0, LastSeek);
    public double Duration => 240;
    public double Volume { get; set; }
    public event EventHandler? Ended;
    public event EventHandler<string>? Failed;
    public Task PlayAsync(AudioSource source, CancellationToken cancellationToken) { LastSeek = 0; IsPlaying = true; return Task.CompletedTask; }
    public void Pause() => IsPlaying = false;
    public void Resume() => IsPlaying = true;
    public void Seek(double seconds) => LastSeek = seconds;
    public void Dispose() { }
}
