using Hibiki.Core.Models;
using Hibiki.Core.Services;
using LibVLCSharp.Shared;
namespace Hibiki.Desktop;

public sealed class VlcAudioPlayer : IAudioPlayer
{
    private LibVLC? _engine;
    private MediaPlayer? _player;
    private double _volume = 0.8;
    public event EventHandler? Ended;
    public event EventHandler<string>? Failed;
    public bool IsPlaying => _player?.IsPlaying ?? false;
    public double Position => Math.Max(0, _player?.Time ?? 0) / 1000d;
    public double Duration => Math.Max(0, _player?.Length ?? 0) / 1000d;
    public double Volume
    {
        get => _volume;
        set { _volume = Math.Clamp(value, 0, 1); if (_player is not null) _player.Volume = (int)(_volume * 100); }
    }
    public Task PlayAsync(AudioSource source, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_engine is null)
        {
            try { LibVLCSharp.Shared.Core.Initialize(); _engine = new LibVLC("--no-video", "--quiet"); }
            catch (Exception ex) { throw new InvalidOperationException("Não foi possível iniciar o LibVLC. No Linux, instale libvlc e os plugins de áudio do VLC.", ex); }
            _player = new MediaPlayer(_engine);
            _player.EndReached += (_, _) => Ended?.Invoke(this, EventArgs.Empty);
            _player.EncounteredError += (_, _) => Failed?.Invoke(this, "Não foi possível reproduzir este áudio. Tente abrir a faixa novamente.");
        }
        _player!.Stop();
        using var media = new Media(_engine, source.Url, Uri.TryCreate(source.Url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" ? FromType.FromLocation : FromType.FromPath);
        if (source.Headers.TryGetValue("User-Agent", out var agent)) media.AddOption(":http-user-agent=" + agent);
        if (source.Headers.TryGetValue("Referer", out var referer)) media.AddOption(":http-referrer=" + referer);
        if (!_player.Play(media)) throw new IOException("O player não conseguiu abrir o áudio.");
        _player.Volume = (int)(_volume * 100);
        return Task.CompletedTask;
    }
    public void Pause() => _player?.SetPause(true);
    public void Resume() => _player?.SetPause(false);
    public void Seek(double seconds) { if (_player is not null) _player.Time = (long)(Math.Clamp(seconds, 0, Duration) * 1000); }
    public void Dispose() { _player?.Dispose(); _engine?.Dispose(); }
}
