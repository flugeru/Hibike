using Hibike.Core.Models;
using Hibike.Core.Services;
using LibVLCSharp.Shared;
namespace Hibike.Desktop;

public sealed class VlcAudioPlayer : IAudioPlayer
{
    private LibVLC? _engine;
    private MediaPlayer? _player;
    private double _volume = 0.8;
    private readonly object _commands = new();
    private Task _tail = Task.CompletedTask;
    private bool _disposed;
    private volatile bool _isPlaying;
    private static readonly AudioOutputInfo NoOutput = new();
    private static readonly AudioOutputInfo ActiveOutput = new(Note: "O LibVLC não informa o formato entregue à saída de áudio.");
    private AudioOutputInfo? _audioOutput;
    public AudioOutputInfo AudioOutput => Volatile.Read(ref _audioOutput) ?? NoOutput;
    private long _position, _duration;
    public event EventHandler? Ended;
    public event EventHandler<string>? Failed;
    public bool IsPlaying => _isPlaying;
    public double Position => Math.Max(0, Interlocked.Read(ref _position)) / 1000d;
    public double Duration => Math.Max(0, Interlocked.Read(ref _duration)) / 1000d;
    public AudioSource? PlaybackSource { get; private set; }
    public double Volume
    {
        get => _volume;
        set { if (!double.IsFinite(value)) return; _volume = Math.Clamp(value, 0, 1); Send(() => { if (_player is not null) _player.Volume = (int)(_volume * 100); }); }
    }
    // Native calls stay serialized off the UI thread; cancellation never releases ownership of an in-flight VLC call.
    private Task QueueCommand(Func<Task> action)
    {
        lock (_commands)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var previous = _tail;
            return _tail = Task.Run(async () =>
            {
                try { await previous.ConfigureAwait(false); } catch { /* Each caller observes its own failure. */ }
                await action().ConfigureAwait(false);
            });
        }
    }
    private void Send(Action action)
    {
        lock (_commands)
        {
            if (_disposed) return;
            _ = ObserveAsync(QueueCommand(() => { action(); return Task.CompletedTask; }));
        }
    }
    private async Task ObserveAsync(Task command)
    {
        try { await command.ConfigureAwait(false); }
        catch (Exception e) { PlaybackDiagnostics.Write("VLC", e.Message); Failed?.Invoke(this, "Falha no player: " + e.Message); }
    }
    // Called on the command queue, never from a native callback or the UI. VLC warns that
    // its initial device ID can be unknown; only publish a matching enumerated device.
    private void RefreshAudioOutput()
    {
        var snapshot = Volatile.Read(ref _audioOutput);
        if (snapshot is null || _player is null) return;
        string? device = null;
        try
        {
            if (_player.OutputDevice is { Length: > 0 } id)
                device = _player.AudioOutputDeviceEnum.FirstOrDefault(d => d.DeviceIdentifier == id).Description;
        }
        catch (Exception e) { PlaybackDiagnostics.Write("Saída do VLC", e.Message); }
        // End/error can arrive during enumeration. Do not revive the old route.
        Interlocked.CompareExchange(ref _audioOutput, string.IsNullOrWhiteSpace(device) ? ActiveOutput : ActiveOutput with { Device = device }, snapshot);
    }
    private void ClearAudioOutput() => Volatile.Write(ref _audioOutput, null);
    public Task PlayAsync(AudioSource source, CancellationToken cancellationToken) => QueueCommand(async () =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_engine is null)
        {
            PlaybackDiagnostics.Write("VLC", "Iniciando motor nativo.");
            var startup = System.Diagnostics.Stopwatch.StartNew();
            try { LibVLCSharp.Shared.Core.Initialize(); _engine = new LibVLC("--no-video", "--quiet"); }
            catch (Exception ex) { throw new InvalidOperationException("Não foi possível iniciar o LibVLC. No Linux, instale libvlc e os plugins de áudio do VLC.", ex); }
            _player = new MediaPlayer(_engine);
            _player.TimeChanged += (_, e) => Interlocked.Exchange(ref _position, e.Time);
            _player.LengthChanged += (_, e) => Interlocked.Exchange(ref _duration, e.Length);
            _player.Playing += (_, _) =>
            {
                _isPlaying = true;
                Volatile.Write(ref _audioOutput, ActiveOutput);
                Send(RefreshAudioOutput);
            };
            _player.AudioDevice += (_, _) => Send(RefreshAudioOutput);
            _player.Paused += (_, _) => _isPlaying = false;
            _player.Stopped += (_, _) => { _isPlaying = false; ClearAudioOutput(); };
            _player.EndReached += (_, _) => { _isPlaying = false; ClearAudioOutput(); Ended?.Invoke(this, EventArgs.Empty); };
            _player.EncounteredError += (_, _) => { _isPlaying = false; ClearAudioOutput(); Failed?.Invoke(this, "Não foi possível reproduzir este áudio. Tente abrir a faixa novamente."); };
            PlaybackDiagnostics.Write("VLC", $"Inicialização: {startup.ElapsedMilliseconds} ms");
        }
        var watch = System.Diagnostics.Stopwatch.StartNew();
        PlaybackDiagnostics.Write("VLC", "Parando mídia anterior.");
        _player!.Stop();
        ClearAudioOutput();
        PlaybackDiagnostics.Write("VLC", $"Stop: {watch.ElapsedMilliseconds} ms");
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Exchange(ref _position, 0); Interlocked.Exchange(ref _duration, 0);
        using var media = new Media(_engine, source.Url, Uri.TryCreate(source.Url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" ? FromType.FromLocation : FromType.FromPath);
        PlaybackSource = source;
        if (File.Exists(source.Url))
        {
            var status = await media.Parse(MediaParseOptions.ParseLocal, 5000, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (status == MediaParsedStatus.Done)
            {
                var audio = media.Tracks.FirstOrDefault(t => t.TrackType == TrackType.Audio);
                var codec = System.Text.Encoding.ASCII.GetString(BitConverter.GetBytes(audio.Codec)).TrimEnd('\0', ' ');
                PlaybackSource = source with
                {
                    Title = media.Meta(MetadataType.Title) is { Length: > 0 } title ? title : source.Title,
                    Artist = media.Meta(MetadataType.Artist) is { Length: > 0 } artist ? artist : source.Artist,
                    Artwork = source.Artwork ?? (media.Meta(MetadataType.ArtworkURL) is { Length: > 0 } art && !art.StartsWith("attachment:") ? art : null),
                    BitrateKbps = audio.Bitrate > 0 ? audio.Bitrate / 1000d : null,
                    Codec = string.IsNullOrWhiteSpace(codec) ? null : codec,
                    SampleRateHz = audio.Data.Audio.Rate > 0 ? audio.Data.Audio.Rate : null,
                    Channels = audio.Data.Audio.Channels > 0 ? audio.Data.Audio.Channels : null,
                    Container = Path.GetExtension(source.Url).TrimStart('.').ToLowerInvariant()
                };
            }
            else PlaybackDiagnostics.Write("Arquivo local", "Não foi possível ler metadados: " + status);
            using var file = File.OpenRead(source.Url);
            var header = new byte[4];
            if (file.Length >= 4) file.ReadExactly(header);
            if (header.AsSpan().SequenceEqual("fLaC"u8))
            {
                ulong streamInfo = 0;
                bool last;
                do
                {
                    file.ReadExactly(header); last = (header[0] & 128) != 0;
                    var length = (header[1] << 16) | (header[2] << 8) | header[3];
                    if ((header[0] & 127) == 0 && length == 34)
                    {
                        var block = new byte[34]; file.ReadExactly(block);
                        streamInfo = System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(block.AsSpan(10, 8));
                    }
                    else file.Seek(length, SeekOrigin.Current);
                    if (file.Position > file.Length) throw new InvalidDataException("Metadados FLAC truncados.");
                } while (!last);
                var rate = streamInfo >> 44;
                var samples = streamInfo & 0xFFFFFFFFFUL;
                PlaybackSource = PlaybackSource! with
                {
                    SampleRateHz = rate > 0 ? rate : null,
                    Channels = ((streamInfo >> 41) & 7) + 1,
                    BitDepth = (int)((streamInfo >> 36) & 31) + 1,
                    BitrateKbps = samples > 0 && rate > 0 ? (file.Length - file.Position) * 8d * rate / samples / 1000 : null
                };
            }
        }
        if (source.Headers.TryGetValue("User-Agent", out var agent)) media.AddOption(":http-user-agent=" + agent);
        if (source.Headers.TryGetValue("Referer", out var referer)) media.AddOption(":http-referrer=" + referer);
        cancellationToken.ThrowIfCancellationRequested();
        watch.Restart();
        PlaybackDiagnostics.Write("VLC", "Abrindo mídia.");
        if (!_player.Play(media)) throw new IOException("O player não conseguiu abrir o áudio.");
        PlaybackDiagnostics.Write("VLC", $"Play: {watch.ElapsedMilliseconds} ms");
        _player.Volume = (int)(_volume * 100);
        if (cancellationToken.IsCancellationRequested) { _player.Stop(); cancellationToken.ThrowIfCancellationRequested(); }
    }).WaitAsync(cancellationToken);
    public Task StopAsync() => QueueCommand(() =>
    {
        _player?.Stop();
        ClearAudioOutput();
        _isPlaying = false;
        PlaybackSource = null;
        Interlocked.Exchange(ref _position, 0);
        Interlocked.Exchange(ref _duration, 0);
        return Task.CompletedTask;
    });
    public void Pause() => Send(() => _player?.SetPause(true));
    public void Resume() => Send(() => _player?.SetPause(false));
    public void Seek(double seconds) { if (double.IsFinite(seconds)) Send(() => { if (_player is not null) _player.Time = (long)(Math.Clamp(seconds, 0, Duration) * 1000); }); }
    public void Dispose()
    {
        Task tail;
        lock (_commands)
        {
            if (_disposed) return;
            _disposed = true;
            var previous = _tail;
            tail = _tail = Task.Run(async () =>
            {
                try { await previous.ConfigureAwait(false); } catch { }
                _player?.Dispose(); _engine?.Dispose(); _isPlaying = false; ClearAudioOutput();
            });
        }
        try { tail.Wait(TimeSpan.FromSeconds(5)); } catch (AggregateException) { }
    }
}
