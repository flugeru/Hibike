using Android.Content;
using Android.OS;
using Hibiki.Core.Models;
using Hibiki.Core.Services;

namespace Hibiki.Android;

public sealed class AndroidAudioPlayer(Context context) : IAudioPlayer, IAudioPreparation
{
    private PlaybackService? _service;
    private Task<byte[]?>? _artworkTask;
    private double _volume = 0.8;
    public event EventHandler? Ended;
    public event EventHandler<string>? Failed;
    public bool IsPlaying => PlaybackService.Instance?.Playing ?? false;
    public double Position => PlaybackService.Instance?.Position ?? 0;
    public double Duration => PlaybackService.Instance?.Duration ?? 0;
    public double Volume { get => _volume; set { _volume = Math.Clamp(value, 0, 1); Main(() => PlaybackService.Instance?.SetVolume(_volume)); } }
    public async Task BeginPreparationAsync(Track track, CancellationToken cancellationToken)
    {
        // Prepare the album art while YT-DLP resolves the stream. Never wait for it
        // before starting the foreground service or fetching the audio URL.
        _artworkTask = NotificationArtwork.LoadAsync(track.Thumbnail, cancellationToken);
        var intent = new Intent(context, typeof(PlaybackService)).SetAction(PlaybackService.PrepareAction)!.PutExtra("title", track.Title);
        await MainAsync(() => { if (Build.VERSION.SdkInt >= BuildVersionCodes.O) context.StartForegroundService(intent); else context.StartService(intent); }).WaitAsync(cancellationToken);
        while (PlaybackService.Instance is null) await Task.Delay(20, cancellationToken);
        var service = PlaybackService.Instance;
        if (!ReferenceEquals(_service, service))
        {
            if (_service is not null) { _service.Ended -= OnEnded; _service.Failed -= OnFailed; }
            _service = service; _service!.Ended += OnEnded; _service.Failed += OnFailed;
        }
    }
    public async Task PlayAsync(AudioSource source, CancellationToken cancellationToken)
    {
        if (PlaybackService.Instance is null) await BeginPreparationAsync(new("audio", source.Title ?? "Hibiki", source.Artist ?? "", source.Url), cancellationToken);
        var service = PlaybackService.Instance!;
        var started = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            byte[]? artwork = null;
            if (_artworkTask is { } coverTask)
            {
                try { artwork = await coverTask.WaitAsync(TimeSpan.FromMilliseconds(200), cancellationToken); }
                catch (TimeoutException) { /* The cover is optional; start audio immediately. */ }
            }
            await MainAsync(() => service.Open(source, _volume, artwork)).WaitAsync(cancellationToken);
            while (service.State != 3 || !service.Playing)
            {
                if (service.Error is { } error) throw new IOException(error);
                await Task.Delay(25, cancellationToken);
            }
            PlaybackDiagnostics.Write("Player", $"Streaming iniciado em {started.ElapsedMilliseconds} ms; buffer inicial configurado em 250 ms.");
        }
        catch { Main(service.CancelPreparation); throw; }
    }
    public void Pause() => Main(() => PlaybackService.Instance?.PauseAudio());
    public void Resume() => Main(() => PlaybackService.Instance?.ResumeAudio());
    public void Seek(double seconds) => Main(() => PlaybackService.Instance?.Seek(seconds));
    public void CancelPreparation() => Main(() => PlaybackService.Instance?.CancelPreparation());
    private void OnEnded(object? sender, EventArgs args) => Ended?.Invoke(this, args);
    private void OnFailed(object? sender, string error) => Failed?.Invoke(this, error);
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
    public void Dispose() { if (_service is not null) { _service.Ended -= OnEnded; _service.Failed -= OnFailed; } }
}
