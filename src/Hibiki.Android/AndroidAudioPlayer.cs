using Android.Content;
using AudioSource = Hibiki.Core.Models.AudioSource;
using Android.Media;
using Hibiki.Core.Models;
using Hibiki.Core.Services;

namespace Hibiki.Android;

public sealed class AndroidAudioPlayer(Context context) : IAudioPlayer
{
    private MediaPlayer? _player;
    private bool _prepared;
    private double _volume = 0.8;
    public event EventHandler? Ended;
    public event EventHandler<string>? Failed;
    public bool IsPlaying => _prepared && (_player?.IsPlaying ?? false);
    public double Position => _prepared ? (_player?.CurrentPosition ?? 0) / 1000d : 0;
    public double Duration => _prepared ? Math.Max(0, _player?.Duration ?? 0) / 1000d : 0;
    public double Volume
    {
        get => _volume;
        set { _volume = Math.Clamp(value, 0, 1); _player?.SetVolume((float)_volume, (float)_volume); }
    }
    public async Task PlayAsync(AudioSource source, CancellationToken cancellationToken)
    {
        ReleasePlayer();
        var player = new MediaPlayer();
        _player = player;
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        player.SetAudioAttributes(new AudioAttributes.Builder()!.SetUsage(AudioUsageKind.Media)!
            .SetContentType(AudioContentType.Music)!.Build());
        player.Prepared += (_, _) => ready.TrySetResult();
        player.Error += (_, e) =>
        {
            e.Handled = true;
            var message = $"Falha no áudio ({e.What}/{e.Extra}). Tente reproduzir a faixa novamente.";
            if (!ready.TrySetException(new IOException(message))) Failed?.Invoke(this, message);
        };
        player.Completion += (_, _) => Ended?.Invoke(this, EventArgs.Empty);
        try
        {
            if (source.Url.StartsWith("content://", StringComparison.Ordinal))
                player.SetDataSource(context, global::Android.Net.Uri.Parse(source.Url)!);
            else if (Uri.TryCreate(source.Url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                player.SetDataSource(context, global::Android.Net.Uri.Parse(source.Url)!, new Dictionary<string, string>(source.Headers));
            else player.SetDataSource(source.Url);
            player.PrepareAsync();
            await ready.Task.WaitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _prepared = true;
            player.SetVolume((float)_volume, (float)_volume);
            player.Start();
        }
        catch { if (ReferenceEquals(_player, player)) ReleasePlayer(); throw; }
    }
    public void Pause() { if (IsPlaying) _player!.Pause(); }
    public void Resume() { if (_prepared) _player!.Start(); }
    public void Seek(double seconds) { if (_prepared) _player!.SeekTo((int)(Math.Clamp(seconds, 0, Duration) * 1000)); }
    private void ReleasePlayer()
    {
        _prepared = false;
        _player?.Release();
        _player?.Dispose();
        _player = null;
    }
    public void Dispose() => ReleasePlayer();
}
