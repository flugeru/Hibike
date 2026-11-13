using Android.Content;
using Android.OS;
using Hibike.Core.Models;
using Hibike.Core.Services;

namespace Hibike.Android;

public sealed class AndroidAudioPlayer(Context context) : IAudioPlayer, IAudioPreparation, IAudioPlaybackStateNotifications
{
    private PlaybackService? _service;
    private double _volume = 0.8;
    public event EventHandler? Ended;
    public event EventHandler<string>? Failed;
    public event EventHandler? PlaybackStateChanged;
    public bool IsPlaying => PlaybackService.Instance?.Playing ?? false;
    public bool IsBuffering => PlaybackService.Instance?.Buffering == true;
    public double Position => PlaybackService.Instance?.Position ?? 0;
    public double Duration => PlaybackService.Instance?.Duration ?? 0;
    public AudioSource? PlaybackSource { get; private set; }
    public double Volume { get => _volume; set { _volume = Math.Clamp(value, 0, 1); Main(() => PlaybackService.Instance?.SetVolume(_volume)); } }
    public Task BeginPreparationAsync(Track track, CancellationToken cancellationToken) => PrepareAsync(track, cancellationToken, reuseService: false);
    public Task BeginRecoveryAsync(Track track, CancellationToken cancellationToken) => PrepareAsync(track, cancellationToken, reuseService: true);

    private async Task PrepareAsync(Track track, CancellationToken cancellationToken, bool reuseService)
    {
        if (!reuseService || PlaybackService.Instance is null)
        {
            var intent = new Intent(context, typeof(PlaybackService)).SetAction(PlaybackService.PrepareAction)!.PutExtra("title", track.Title);
            await MainAsync(() => { if (Build.VERSION.SdkInt >= BuildVersionCodes.O) context.StartForegroundService(intent); else context.StartService(intent); }).WaitAsync(cancellationToken);
            while (PlaybackService.Instance is null) await Task.Delay(20, cancellationToken);
        }
        var service = PlaybackService.Instance;
        if (!ReferenceEquals(_service, service))
        {
            UnsubscribeFromService();
            _service = service;
            if (_service is null) return;
            _service.Ended += OnEnded;
            _service.Failed += OnFailed;
            _service.PlaybackStateChanged += OnPlaybackStateChanged;
        }
    }
    public async Task PlayAsync(AudioSource source, CancellationToken cancellationToken)
    {
        PlaybackSource = null;
        if (Path.IsPathFullyQualified(source.Url) && File.Exists(source.Url))
            source = await Task.Run(() => AndroidMetadata.ReadAudioSource(source), cancellationToken);
        if (PlaybackService.Instance is null) await BeginPreparationAsync(new("audio", source.Title ?? "Hibike", source.Artist ?? "", source.Url), cancellationToken);
        var service = PlaybackService.Instance!;
        var started = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var artwork = await NotificationArtwork.LoadAsync(source.Artwork, cancellationToken);
            await MainAsync(() => service.Open(source, _volume, artwork)).WaitAsync(cancellationToken);
            while (service.State != 3 || !service.Playing)
            {
                if (service.Error is { } error) throw new IOException(error);
                await Task.Delay(25, cancellationToken);
            }
            PlaybackDiagnostics.Write("Player", $"Streaming iniciado em {started.ElapsedMilliseconds} ms; buffer inicial configurado em 250 ms.");
            PlaybackSource = source;
        }
        catch { Main(service.CancelPreparation); throw; }
    }
    public Task StopAsync() => MainAsync(() => { PlaybackService.Instance?.StopAudio(); PlaybackSource = null; });
    public void Pause() => Main(() => PlaybackService.Instance?.PauseAudio());
    public void Resume() => Main(() => PlaybackService.Instance?.ResumeAudio());
    public void Seek(double seconds) => Main(() => PlaybackService.Instance?.Seek(seconds));
    public void CancelPreparation() => Main(() => PlaybackService.Instance?.CancelPreparation());
    private void OnEnded(object? sender, EventArgs args) => Ended?.Invoke(this, args);
    private void OnFailed(object? sender, string error) => Failed?.Invoke(this, error);
    private void OnPlaybackStateChanged(object? sender, EventArgs args) => PlaybackStateChanged?.Invoke(this, args);
    private static Task MainAsync(Action action)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Main(() => { try { action(); ready.TrySetResult(); } catch (Exception ex) { ready.TrySetException(ex); } });
        return ready.Task;
    }
    private static void Main(Action action)
    {
        if (Looper.MyLooper() == Looper.MainLooper) action(); else new Handler(Looper.MainLooper!).Post(action);
    }
    public void Dispose() => UnsubscribeFromService();

    private void UnsubscribeFromService()
    {
        if (_service is null) return;
        _service.Ended -= OnEnded;
        _service.Failed -= OnFailed;
        _service.PlaybackStateChanged -= OnPlaybackStateChanged;
        _service = null;
    }
}
