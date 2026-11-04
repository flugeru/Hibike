using Hibike.Core.Models;
using Hibike.Core.Services;
using NAudio.Wave;

namespace Hibike.Desktop;

public sealed class WindowsAudioPlayer(bool mediaFoundation) : IAudioPlayer
{
    private readonly object _gate = new();
    private WaveStream? _reader;
    private WaveOutEvent? _output;
    private bool _disposed;
    private double _volume = .8;
    public bool IsPlaying { get { lock (_gate) return _output?.PlaybackState == PlaybackState.Playing; } }
    public double Position { get { lock (_gate) return _reader?.CurrentTime.TotalSeconds ?? 0; } }
    public double Duration { get { lock (_gate) return _reader?.TotalTime.TotalSeconds ?? 0; } }
    public AudioSource? PlaybackSource { get; private set; }
    public event EventHandler? Ended;
    public event EventHandler<string>? Failed;
    public double Volume { get { lock (_gate) return _volume; } set { if (double.IsFinite(value)) lock (_gate) { _volume = Math.Clamp(value, 0, 1); if (_output is not null) _output.Volume = (float)_volume; } } }
    public Task PlayAsync(AudioSource source, CancellationToken cancellationToken) => Task.Run(() =>
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this); cancellationToken.ThrowIfCancellationRequested(); Release();
            // ponytail: URL decoding uses Windows codecs; use libmpv/VLC for formats unsupported by Media Foundation.
            if (!File.Exists(source.Url) && source.Headers.Count > 0)
                throw new NotSupportedException("Este motor não aplica cabeçalhos HTTP personalizados. Selecione libmpv ou VLC.");
            try
            {
                _reader = mediaFoundation || !File.Exists(source.Url) ? new MediaFoundationReader(source.Url) : new AudioFileReader(source.Url);
                var output = _output = new WaveOutEvent(); output.Init(_reader); output.Volume = (float)_volume;
                output.PlaybackStopped += (_, e) =>
                {
                    bool end;
                    lock (_gate) { if (_output != output || _disposed) return; end = e.Exception is null && _reader is not null && _reader.Position >= _reader.Length; }
                    if (e.Exception is not null) Failed?.Invoke(this, "Falha na saída de áudio Windows.");
                    else if (end) Ended?.Invoke(this, EventArgs.Empty);
                };
                cancellationToken.ThrowIfCancellationRequested(); PlaybackSource = source; output.Play();
                if (cancellationToken.IsCancellationRequested) { Release(); cancellationToken.ThrowIfCancellationRequested(); }
            }
            catch { Release(); throw; }
        }
    }, cancellationToken);
    public void Pause() { lock (_gate) _output?.Pause(); }
    public void Resume() { lock (_gate) _output?.Play(); }
    public void Seek(double seconds) { if (double.IsFinite(seconds)) lock (_gate) { if (_reader is not null) _reader.CurrentTime = TimeSpan.FromSeconds(Math.Clamp(seconds, 0, _reader.TotalTime.TotalSeconds)); } }
    private void Release() { var output = _output; _output = null; output?.Dispose(); _reader?.Dispose(); _reader = null; }
    public void Dispose() { lock (_gate) { if (_disposed) return; _disposed = true; Release(); } }
}
