using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Media3.Common;
using AndroidX.Media3.DataSource;
using AndroidX.Media3.ExoPlayer;
using AndroidX.Media3.ExoPlayer.Source;
using AndroidX.Media3.Session;
using Avalonia.Threading;
using Hibike.App;
using Hibike.Core.Models;
using Hibike.Core.Services;
using AudioSource = Hibike.Core.Models.AudioSource;

namespace Hibike.Android;

[Service(Name = "com.flugel.hibike.PlaybackService", Exported = true,
    ForegroundServiceType = ForegroundService.TypeMediaPlayback)]
[IntentFilter(new[] { "androidx.media3.session.MediaLibraryService", "android.media.browse.MediaBrowserService" })]
public sealed partial class PlaybackService : MediaLibraryService
{
    public const string PrepareAction = "com.flugel.hibike.PREPARE";
    public static PlaybackService? Instance { get; private set; }
    private IExoPlayer? _player;
    private MediaLibrarySession? _session;
    private LibraryCallback? _libraryCallback;
    private SessionPlayer? _sessionPlayer;
    private Handler? _handler;
    private Action? _poll;
    private double _gain = 1;
    private global::Android.Media.Audiofx.Equalizer? _equalizer;
    private EqualizerPreset _equalizerPreset;
    private int _equalizerSession;
    private DateTime? _sleepDeadline;
    public void SetSleepDeadline(DateTime? utc) => _sleepDeadline = utc;
    private int _state;
    private string? _lastError;
    public global::Android.Media.Session.MediaSession.Token? PlatformToken => _session?.PlatformToken;
    public bool Playing { get; private set; }
    public double Position { get; private set; }
    public double Duration { get; private set; }
    public double BufferedPosition { get; private set; }
    public int State { get; private set; }
    public string? Error { get; private set; }
    public event EventHandler? Ended;
    public event EventHandler<string>? Failed;

    public override void OnCreate()
    {
        base.OnCreate();
        var control = new DefaultLoadControl.Builder().SetBufferDurationsMs(2000, 10000, 250, 1000)!
            .SetPrioritizeTimeOverSizeThresholds(true)!.Build();
        var attributes = new AndroidX.Media3.Common.AudioAttributes.Builder().SetUsage(C.UsageMedia)!.SetContentType(C.AudioContentTypeMusic)!.Build();
        _player = new ExoPlayerBuilder(this).SetLooper(Looper.MainLooper)!.SetLoadControl(control)!
            .SetAudioAttributes(attributes, true)!.SetHandleAudioBecomingNoisy(true)!.SetWakeMode(2)!.Build()!;
        _sessionPlayer = new SessionPlayer(_player);
        var launch = PackageManager!.GetLaunchIntentForPackage(PackageName!)!.PutExtra("open_player", true);
        var pending = PendingIntent.GetActivity(this, 0, launch, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        _libraryCallback = new LibraryCallback();
        _session = new MediaLibrarySession.Builder(this, _sessionPlayer, _libraryCallback).SetSessionActivity(pending)!.Build();
        RefreshFavoriteAction();
        var provider = new DefaultMediaNotificationProvider.Builder(this).SetNotificationId(42)!
            .SetChannelId("hibike-playback")!.SetChannelName(Resource.String.playback_channel)!.Build()!;
        provider.SetSmallIcon(Resource.Drawable.ic_stat_music);
        SetMediaNotificationProvider(provider);
        _handler = new Handler(Looper.MainLooper!);
        _poll = () =>
        {
            if (_player is null) return;
            if (_sleepDeadline is { } deadline && DateTime.UtcNow >= deadline)
            { _sleepDeadline = null; _player.Pause(); }
            Playing = _player.IsPlaying; Position = Math.Max(0, _player.CurrentPosition) / 1000d;
            BufferedPosition = Math.Max(0, _player.BufferedPosition) / 1000d;
            Duration = Math.Max(0, _player.Duration) / 1000d; State = _player.PlaybackState;
            if (State == 3) RefreshEqualizer();
            RefreshFavoriteAction();
            Error = _player.PlayerError?.Message;
            if (Error is not null && Error != _lastError) { _lastError = Error; Failed?.Invoke(this, Error); }
            if (State == 4 && _state != 4) Ended?.Invoke(this, EventArgs.Empty);
            _state = State;
            _handler?.PostDelayed(_poll!, 100);
        };
        _handler.Post(_poll); Instance = this;
    }
    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (intent?.Action == PrepareAction)
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                ((NotificationManager)GetSystemService(NotificationService)!).CreateNotificationChannel(new NotificationChannel("hibike-playback", "Reprodução de música", NotificationImportance.Low));
            var builder = Build.VERSION.SdkInt >= BuildVersionCodes.O ? new Notification.Builder(this, "hibike-playback") : new Notification.Builder(this);
            var notification = builder.SetContentTitle(intent.GetStringExtra("title") ?? "Hibike")!
                .SetContentText("Preparando áudio…")!.SetSmallIcon(Resource.Drawable.ic_stat_music)!
                .SetCategory("transport")!.SetVisibility(NotificationVisibility.Public)!
                .SetStyle(new Notification.MediaStyle().SetMediaSession(_session!.PlatformToken))!
                .SetOngoing(true)!.Build();
            StartForeground(42, notification);
        }
        return base.OnStartCommand(intent, flags, startId);
    }
    // Android generates one Java wrapper per method name and parameters. Emit the covariant binding first.
    public override MediaLibrarySession? OnGetSessionFromMediaLibraryService(MediaSession.ControllerInfo? controllerInfo) => _session;
    public override MediaSession? OnGetSession(MediaSession.ControllerInfo? controllerInfo) => _session;
    public void Open(AudioSource source, double volume, byte[]? artwork = null)
    {
        Error = null; _lastError = null; State = 2;
        var metadata = new MediaMetadata.Builder().SetTitle(source.Title ?? "Hibike")!.SetArtist(source.Artist ?? "")!;
        if (artwork is { Length: > 0 }) metadata.MaybeSetArtworkData(artwork, 3);
        else if (source.Artwork is not null) metadata.SetArtworkUri(global::Android.Net.Uri.Parse(source.Artwork));
        var item = new MediaItem.Builder().SetUri(source.Url)!.SetMediaMetadata(metadata.Build())!.Build();
        var http = new DefaultHttpDataSource.Factory();
        http.SetDefaultRequestProperties(new Dictionary<string, string>(source.Headers));
        var data = new DefaultDataSource.Factory(this, http);
        var media = new DefaultMediaSourceFactory(data).CreateMediaSource(item);
        if (_equalizerPreset != source.Equalizer)
        {
            _equalizerPreset = source.Equalizer;
            ReleaseEqualizer();
        }
        _gain = source.GainDb is { } gain ? Math.Pow(10, Math.Clamp(gain, -18, 6) / 20) : 1;
        SetVolume(volume); _player!.SetMediaSource(media);
        // Register after playable item exists so Media3 replaces preparation notification id 42.
        if (!IsSessionAdded(_session!)) AddSession(_session!);
        _player.Prepare(); _player.Play();
    }
    public void PauseAudio() => _player?.Pause();
    public void ResumeAudio() { if (_player?.PlaybackState == 4) _player.SeekTo(0); _player?.Play(); }
    public void Seek(double seconds) => _player?.SeekTo((long)(Math.Max(0, seconds) * 1000));
    public void SetVolume(double volume) { if (_player is not null) _player.Volume = (float)Math.Clamp(volume * _gain, 0, 1); }
    public void CancelPreparation()
    {
        if (!Playing) { _player?.Stop(); StopForeground(StopForegroundFlags.Remove); }
    }
    public override void OnDestroy()
    {
        Instance = null;
        if (_poll is not null) _handler?.RemoveCallbacks(_poll);
        ReleaseEqualizer();
        _session?.Release(); _libraryCallback?.Dispose(); _sessionPlayer?.Dispose(); _player?.Release(); _player?.Dispose(); _player = null;
        base.OnDestroy();
    }
    private sealed class SessionPlayer(IPlayer player) : ForwardingPlayer(player)
    {
        private bool _openingFromCar;
        public override void SetMediaItems(IList<MediaItem>? items, int startIndex, long startPositionMs)
        {
            if (items is not { Count: > 0 }) return;
            OpenFromCar(items[Math.Clamp(startIndex, 0, items.Count - 1)]);
        }
        public override void SetMediaItem(MediaItem? item, long startPositionMs) => OpenFromCar(item);
        public override void SetMediaItem(MediaItem? item) => OpenFromCar(item);
        public override void SetMediaItem(MediaItem? item, bool resetPosition) => OpenFromCar(item);
        public override void SetMediaItems(IList<MediaItem>? items) => SetMediaItems(items, 0, 0);
        public override void SetMediaItems(IList<MediaItem>? items, bool resetPosition) => SetMediaItems(items, 0, 0);
        public override void Play() { if (!_openingFromCar) base.Play(); }

        private void OpenFromCar(MediaItem? item)
        {
            var controller = AppServices.Controller;
            if (controller is null || item?.MediaId is not { } id || FindLibraryTrack(controller, id) is not { } track) return;
            _openingFromCar = true;
            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    if (controller.Current?.Source == track.Source && !controller.LoadingAudio) { if (!controller.Player.IsPlaying) await controller.ToggleAsync(); }
                    else
                    {
                        controller.ReplaceQueue([track]);
                        await controller.PlayAsync(track, 0);
                    }
                }
                finally { _openingFromCar = false; }
            });
        }

        public override bool IsCommandAvailable(int command) => AvailableCommands?.Contains(command) ?? false;
        public override PlayerCommands? AvailableCommands => new PlayerCommands.Builder().AddAll(base.AvailableCommands)!.AddAll(6, 7, 8, 9)!.Build();
        public override void SeekToNext() => Next();
        public override void SeekToNextMediaItem() => Next();
        public override void SeekToPrevious() => Previous();
        public override void SeekToPreviousMediaItem() => Previous();
        private static void Next() => Dispatcher.UIThread.Post(async () => { if (AppServices.Controller is { } c) await c.NextAsync(); });
        private static void Previous() => Dispatcher.UIThread.Post(async () => { if (AppServices.Controller is { } c) await c.PreviousAsync(); });
    }
}
