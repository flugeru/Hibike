using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Hibiki.App;
using Hibiki.App.Views;
using Hibiki.Core.Models;
using Hibiki.Core.Services;

var directory = Path.Combine(Path.GetTempPath(), "hibiki-ui-smoke-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
AppServices.Configure(new FakeCatalog(), new FakePlayer(), directory);
Console.WriteLine("Starting the Avalonia headless session.");
using var session = HeadlessUnitTestSession.StartNew(typeof(SmokeAppBuilder));
await session.Dispatch(() =>
{
    Console.WriteLine("Building the Hibiki views.");
    var controller = AppServices.CreateController();
    foreach (var width in new[] { 360d, 430d })
    {
        var view = new MainView(controller);
        var window = new Window { Width = width, Height = 780, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        if (view.Bounds.Width < width - 1 || view.Bounds.Height <= 0)
            throw new InvalidOperationException($"Main view has invalid bounds at {width}dp: {view.Bounds}.");
        window.Close();
    }
    Console.WriteLine("Avalonia started and laid out at 360dp and 430dp.");
}, CancellationToken.None);
Directory.Delete(directory, true);

public static class SmokeAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

internal sealed class FakeCatalog : IMusicCatalog
{
    public Task<IReadOnlyList<Track>> SearchAsync(string query, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Track>>([]);
    public Task<AudioSource> ResolveAsync(Track track, CancellationToken cancellationToken) =>
        Task.FromResult(new AudioSource(track.Source, new Dictionary<string, string>()));
}

internal sealed class FakePlayer : IAudioPlayer
{
    public bool IsPlaying { get; private set; }
    public double Position => 0;
    public double Duration => 240;
    public double Volume { get; set; }
    public event EventHandler? Ended;
    public event EventHandler<string>? Failed;
    public Task PlayAsync(AudioSource source, CancellationToken cancellationToken) { IsPlaying = true; return Task.CompletedTask; }
    public void Pause() => IsPlaying = false;
    public void Resume() => IsPlaying = true;
    public void Seek(double seconds) { }
    public void Dispose() { }
}
