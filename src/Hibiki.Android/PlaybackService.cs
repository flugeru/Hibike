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
    public const string CancelPrepareAction = "com.flugel.hibiki.CANCEL_PREPARE";
    public static PlaybackService? Instance { get; private set; }
    private IExoPlayer? _player;
    private MediaSession? _session;
    private PendingIntent? _launchIntent;
    private SessionPlayer? _sessionPlayer;
    private static readonly Lazy<global::Android.Graphics.Bitmap?> PlaceholderCover = new(() =>
    {
        var png = NotificationArtwork.Fallback;
        return global::Android.Graphics.BitmapFactory.DecodeByteArray(png, 0, png.Length);
    });
    private Handler? _handler;
    private Action? _poll;
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
        _launchIntent = PendingIntent.GetActivity(this, 0, launch, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        _session = new MediaSession.Builder(this, _sessionPlayer).SetSessionActivity(_launchIntent)!.Build();
        var provider = new DefaultMediaNotificationProvider.Builder(this).SetNotificationId(42)!
            .SetChannelId("hibiki-playback")!.SetChannelName(Resource.String.playback_channel)!.Build()!;
        provider.SetSmallIcon(Resource.Drawable.ic_stat_music);
        SetMediaNotificationProvider(provider);
        _handler = new Handler(Looper.MainLooper!);
        _poll = () =>
        {
            if (_player is null) return;
            Playing = _player.IsPlaying; Position = Math.Max(0, _player.CurrentPosition) / 1000d;
            BufferedPosition = Math.Max(0, _player.BufferedPosition) / 1000d;
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
        if (intent?.Action == CancelPrepareAction)
        {
            Dispatcher.UIThread.Post(() => AppServices.Controller?.CancelPlayback());
            CancelPreparation();
            return StartCommandResult.NotSticky;
        }
        if (intent?.Action == PrepareAction)
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            {
                var channel = new NotificationChannel("hibiki-playback", GetString(Resource.String.playback_channel), NotificationImportance.Low);
                channel.Description = GetString(Resource.String.playback_channel_description);
                channel.SetShowBadge(false);
                ((NotificationManager)GetSystemService(NotificationService)!).CreateNotificationChannel(channel);
            }
            // A regular foreground notification is not a music player on Android/HyperOS.
            // Attach the platform MediaSession even while yt-dlp is resolving the URL.
            // Media3 takes over this SAME notification id once the track starts.
            var cancelIntent = new Intent(this, typeof(PlaybackService)).SetAction(CancelPrepareAction);
            var cancel = PendingIntent.GetService(this, 42, cancelIntent,
                PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
            var style = new Notification.MediaStyle().SetMediaSession(_session!.PlatformToken);
            style.SetShowActionsInCompactView(0);
            var builder = Build.VERSION.SdkInt >= BuildVersionCodes.O
                ? new Notification.Builder(this, "hibiki-playback")
                : new Notification.Builder(this);
            builder.SetContentTitle(intent.GetStringExtra("title") ?? "Hibiki")!
                .SetContentText(intent.GetStringExtra("artist") ?? GetString(Resource.String.preparing_audio))!
                .SetSubText(GetString(Resource.String.preparing_audio))!
                .SetSmallIcon(Resource.Drawable.ic_stat_music)!
                .SetLargeIcon(PlaceholderCover.Value)!
                .SetContentIntent(_launchIntent)!
                .SetColor(global::Android.Graphics.Color.Rgb(189, 162, 255))!
                .SetCategory("transport")!
                .SetVisibility(NotificationVisibility.Public)!
                .SetOnlyAlertOnce(true)!
                .SetShowWhen(false)!
                .SetOngoing(true)!
                .AddAction(Resource.Drawable.ic_notification_cancel, GetString(Resource.String.cancel_preparation), cancel)!
                .SetStyle(style);
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O) builder.SetColorized(true);
            StartForeground(42, builder.Build());
        }
        return base.OnStartCommand(intent, flags, startId);
    }
    public override MediaSession? OnGetSession(MediaSession.ControllerInfo controllerInfo) => _session;
    public void Open(AudioSource source, double volume, byte[]? artwork)
    {
        Error = null; _lastError = null; State = 2;
        var metadata = new MediaMetadata.Builder().SetTitle(source.Title ?? "Hibiki")!.SetArtist(source.Artist ?? "")!;
        // Passing compressed artwork *data* (not just a remote URL) lets the Android 13+
        // media card and lock screen reliably show the cover and derive their colors.
        // Picture type 3 = front cover (ID3/APIC).
        metadata.MaybeSetArtworkData(artwork ?? NotificationArtwork.Fallback, 3);
        var item = new MediaItem.Builder().SetUri(source.Url)!.SetMediaMetadata(metadata.Build())!.Build();
        var http = new DefaultHttpDataSource.Factory();
        http.SetDefaultRequestProperties(new Dictionary<string, string>(source.Headers));
        var data = new DefaultDataSource.Factory(this, http);
        var media = new DefaultMediaSourceFactory(data).CreateMediaSource(item);
        _player!.Volume = (float)volume;
        _player.SetMediaSource(media);
        // Register only after a playable item exists. Registering in OnCreate
        // with an empty playlist lets Media3 remove the preparation notification.
        // The app controls ExoPlayer directly, so onGetSession() is not guaranteed
        // to register the session for notification state observation.
        if (!IsSessionAdded(_session!)) AddSession(_session!);
        _player.Prepare();
        _player.Play();
    }
    public void PauseAudio() => _player?.Pause();
    public void ResumeAudio() { if (_player?.PlaybackState == 4) _player.SeekTo(0); _player?.Play(); }
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
