using System.Reflection;
using Avalonia.Threading;
using Hibike.App;
using Hibike.App.Views;
using Hibike.Core.Models;
using Hibike.Core.Services;

internal static partial class Program
{
    private static async Task RunRecoveryChecksAsync(string directory, MainView view)
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        var path = Path.Combine(directory, "recovery");
        var player = new RecoveryPlayer();
        var music = new MusicController(new Catalog(), player, new LibraryStore(path), path);
        var online = true;
        var previousNetwork = AppServices.IsNetworkAvailable;
        var previousStorage = AppServices.DiscordTokenStorage;
        AppServices.IsNetworkAvailable = () => online;
        void TickAt(DateTime now) => typeof(MusicController).GetMethod("ObservePlaybackRecovery", Hidden)!.Invoke(music, [now]);
        try
        {
            music.SetAutoplay(false);
            var first = new Track("first", "First", "Artist", "https://example.org/first");
            var second = new Track("second", "Second", "Artist", "https://example.org/second");
            music.ReplaceQueue([first, second]);
            await music.PlayAsync(first, 0);
            player.Position = 52;
            TickAt(DateTime.UtcNow);
            online = false;
            player.Interrupt();
            await Dispatcher.Yield(DispatcherPriority.Background);
            Check(music.Position == 52 && music.PlaybackRecoveryMessage?.StartsWith("Sem conexão") == true,
                "Offline failure did not preserve position or show a useful message");
            await music.ToggleAsync();
            Check(player.Starts == 1, "Play attempted to fetch audio while offline");

            online = true;
            TickAt(DateTime.UtcNow);
            Check(player.Starts == 1, "Recovery retried before 30 seconds");
            TickAt(DateTime.UtcNow.AddSeconds(31));
            Check(player.Starts == 2 && player.Position == 52 && music.Current == first && music.Upcoming.Single() == second,
                "Automatic recovery changed the track, queue or position");
            Check(music.Listening.Values.Single().Plays == 1, "Recovery inflated playback statistics");

            player.Position = 68;
            TickAt(DateTime.UtcNow);
            player.Interrupt();
            await Dispatcher.Yield(DispatcherPriority.Background);
            music.PausePlayback();
            TickAt(DateTime.UtcNow.AddSeconds(31));
            Check(player.Starts == 2 && music.Position == 68 && music.PlaybackRecoveryMessage is null,
                "Manual pause did not stop automatic recovery");
            await music.ResumePlaybackAsync();
            Check(player.Starts == 3 && player.Position == 68, "Manual Play did not resume the interrupted track");

            player.Position = 72;
            player.Stall();
            await music.ToggleAsync();
            Check(player.Starts == 4 && player.Position == 72, "Play during buffering did not reopen the stream");

            var reads = 0;
            online = false;
            AppServices.DiscordTokenStorage = (() => { reads++; return "invalid token"; }, _ => { }, () => { }, () => true);
            typeof(MainView).GetMethod("StartDiscordRecovery", Hidden)!.Invoke(view, null);
            var timer = (DispatcherTimer)typeof(MainView).GetField("_discordRetryTimer", Hidden)!.GetValue(view)!;
            Check(timer.IsEnabled && timer.Interval == TimeSpan.FromSeconds(30) && reads == 0,
                "Saved Discord credentials did not schedule an offline retry");
            online = true;
            await (Task)typeof(MainView).GetMethod("RestoreDiscordAsync", Hidden)!.Invoke(view, null)!;
            Check(reads == 1 && !timer.IsEnabled, "An invalid saved credential should stop retries");
        }
        finally
        {
            typeof(MainView).GetMethod("StopDiscordRecovery", Hidden)!.Invoke(view, null);
            AppServices.IsNetworkAvailable = previousNetwork;
            AppServices.DiscordTokenStorage = previousStorage;
            music.StopObserving();
            await music.FlushPendingSavesAsync();
        }
    }

    private sealed class RecoveryPlayer : IAudioPlayer
    {
        public int Starts { get; private set; }
        public bool IsPlaying { get; private set; }
        public bool IsBuffering { get; private set; }
        public double Position { get; set; }
        public double Duration => 200;
        public double Volume { get; set; }
        public event EventHandler? Ended { add { } remove { } }
        public event EventHandler<string>? Failed;

        public Task PlayAsync(AudioSource source, CancellationToken cancellationToken)
        {
            Starts++;
            Position = 0;
            IsBuffering = false;
            IsPlaying = true;
            return Task.CompletedTask;
        }
        public void Pause() { IsPlaying = false; IsBuffering = false; }
        public void Resume() => IsPlaying = true;
        public void Seek(double seconds) => Position = seconds;
        public void Dispose() { }
        public void Stall() { IsPlaying = false; IsBuffering = true; }
        public void Interrupt() { IsPlaying = false; IsBuffering = false; Position = 0; Failed?.Invoke(this, "Simulated network error"); }
    }
}
