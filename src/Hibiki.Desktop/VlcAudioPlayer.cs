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
    public AudioSource? PlaybackSource { get; private set; }
    public double Volume
    {
        get => _volume;
        set { _volume = Math.Clamp(value, 0, 1); if (_player is not null) _player.Volume = (int)(_volume * 100); }
    }
    public async Task PlayAsync(AudioSource source, CancellationToken cancellationToken)
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
        PlaybackSource = source;
        if (File.Exists(source.Url))
        {
            var status = await media.Parse(MediaParseOptions.ParseLocal, 5000, cancellationToken);
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
        if (!_player.Play(media)) throw new IOException("O player não conseguiu abrir o áudio.");
        _player.Volume = (int)(_volume * 100);
    }
    public void Pause() => _player?.SetPause(true);
    public void Resume() => _player?.SetPause(false);
    public void Seek(double seconds) { if (_player is not null) _player.Time = (long)(Math.Clamp(seconds, 0, Duration) * 1000); }
    public void Dispose() { _player?.Dispose(); _engine?.Dispose(); }
}
