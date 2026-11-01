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
using Hibiki.App;
using Hibiki.Core.Models;
using Hibiki.Core.Services;
using AudioSource = Hibiki.Core.Models.AudioSource;

namespace Hibiki.Android;

[Service(Name = "com.flugel.hibiki.PlaybackService", Exported = true,
    ForegroundServiceType = ForegroundService.TypeMediaPlayback)]
[IntentFilter(new[] { "androidx.media3.session.MediaSessionService" })]
public sealed class PlaybackService : MediaSessionService
{
    public const string PrepareAction = "com.flugel.hibiki.PREPARE";
    public static PlaybackService? Instance { get; private set; }
    private IExoPlayer? _player;
    private MediaSession? _session;
    private SessionPlayer? _sessionPlayer;
    private Handler? _handler;
    private Action? _poll;
    private int _state;
    private string? _lastError;
    public bool Playing { get; private set; }
    public double Position { get; private set; }
    public double Duration { get; private set; }
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
        var launch = PackageManager!.GetLaunchIntentForPackage(PackageName!)!;
        var pending = PendingIntent.GetActivity(this, 0, launch, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        _session = new MediaSession.Builder(this, _sessionPlayer).SetSessionActivity(pending)!.Build();
        var provider = new DefaultMediaNotificationProvider.Builder(this).SetNotificationId(42)!
            .SetChannelId("hibiki-playback")!.SetChannelName(Resource.String.playback_channel)!.Build()!;
        provider.SetSmallIcon(Resource.Drawable.ic_stat_music);
        SetMediaNotificationProvider(provider);
        _handler = new Handler(Looper.MainLooper!);
        _poll = () =>
        {
            if (_player is null) return;
            Playing = _player.IsPlaying; Position = Math.Max(0, _player.CurrentPosition) / 1000d;
            Duration = Math.Max(0, _player.Duration) / 1000d; State = _player.PlaybackState;
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
                ((NotificationManager)GetSystemService(NotificationService)!).CreateNotificationChannel(new NotificationChannel("hibiki-playback", "Reprodução de música", NotificationImportance.Low));
            var builder = Build.VERSION.SdkInt >= BuildVersionCodes.O ? new Notification.Builder(this, "hibiki-playback") : new Notification.Builder(this);
            var notification = builder.SetContentTitle(intent.GetStringExtra("title") ?? "Hibiki")!
                .SetContentText("Preparando áudio…")!.SetSmallIcon(Resource.Drawable.ic_stat_music)!.SetOngoing(true)!.Build();
            StartForeground(42, notification);
        }
        return base.OnStartCommand(intent, flags, startId);
    }
    public override MediaSession? OnGetSession(MediaSession.ControllerInfo controllerInfo) => _session;
    public void Open(AudioSource source, double volume)
    {
        Error = null; _lastError = null; State = 2;
        var metadata = new MediaMetadata.Builder().SetTitle(source.Title ?? "Hibiki")!.SetArtist(source.Artist ?? "")!;
        if (source.Artwork is not null) metadata.SetArtworkUri(global::Android.Net.Uri.Parse(source.Artwork));
        var item = new MediaItem.Builder().SetUri(source.Url)!.SetMediaMetadata(metadata.Build())!.Build();
        var http = new DefaultHttpDataSource.Factory();
        http.SetDefaultRequestProperties(new Dictionary<string, string>(source.Headers));
        var data = new DefaultDataSource.Factory(this, http);
        var media = new DefaultMediaSourceFactory(data).CreateMediaSource(item);
        _player!.Volume = (float)volume; _player.SetMediaSource(media); _player.Prepare(); _player.Play();
    }
    public void PauseAudio() => _player?.Pause();
    public void ResumeAudio() => _player?.Play();
    public void Seek(double seconds) => _player?.SeekTo((long)(Math.Max(0, seconds) * 1000));
    public void SetVolume(double volume) { if (_player is not null) _player.Volume = (float)volume; }
    public void CancelPreparation()
    {
        if (!Playing) { _player?.Stop(); StopForeground(StopForegroundFlags.Remove); }
    }
    public override void OnDestroy()
    {
        Instance = null;
        if (_poll is not null) _handler?.RemoveCallbacks(_poll);
        _session?.Release(); _sessionPlayer?.Dispose(); _player?.Release(); _player?.Dispose(); _player = null;
        base.OnDestroy();
    }
    private sealed class SessionPlayer(IPlayer player) : ForwardingPlayer(player)
    {
        public override PlayerCommands? AvailableCommands => new PlayerCommands.Builder().AddAll(base.AvailableCommands)!.AddAll(6, 7, 8, 9)!.Build();
        public override void SeekToNext() => Next();
        public override void SeekToNextMediaItem() => Next();
        public override void SeekToPrevious() => Previous();
        public override void SeekToPreviousMediaItem() => Previous();
        private static void Next() => Dispatcher.UIThread.Post(async () => { if (AppServices.Controller is { } c) await c.NextAsync(); });
        private static void Previous() => Dispatcher.UIThread.Post(async () => { if (AppServices.Controller is { } c) await c.PreviousAsync(); });
    }
}
