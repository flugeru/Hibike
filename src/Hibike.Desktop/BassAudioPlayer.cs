using System.Runtime.InteropServices;
using Hibike.Core.Models;
using Hibike.Core.Services;

namespace Hibike.Desktop;

public sealed class BassAudioPlayer : IAudioPlayer
{
    private readonly object _gate = new();
    private uint _stream;
    private bool _initialized, _disposed, _started;
    private double _volume = .8;
    private Timer? _timer;
    public AudioSource? PlaybackSource { get; private set; }
    public event EventHandler? Ended;
    public event EventHandler<string>? Failed;
    public bool IsPlaying { get { lock (_gate) return _stream != 0 && BASS_ChannelIsActive(_stream) == 1; } }
    public double Position { get { lock (_gate) return Seconds(false); } }
    public double Duration { get { lock (_gate) return Seconds(true); } }
    private double Seconds(bool length) { if (_stream == 0) return 0; var bytes = length ? BASS_ChannelGetLength(_stream, 0) : BASS_ChannelGetPosition(_stream, 0); return bytes == ulong.MaxValue ? 0 : Math.Max(0, BASS_ChannelBytes2Seconds(_stream, bytes)); }
    public double Volume { get { lock (_gate) return _volume; } set { if (double.IsFinite(value)) lock (_gate) { _volume = Math.Clamp(value, 0, 1); if (_stream != 0) Check(BASS_ChannelSetAttribute(_stream, 2, (float)_volume)); } } }
    public Task PlayAsync(AudioSource source, CancellationToken cancellationToken) => Task.Run(() =>
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this); cancellationToken.ThrowIfCancellationRequested();
            if (!_initialized) { Check(BASS_Init(-1, 44100, 0, IntPtr.Zero, IntPtr.Zero)); _initialized = true; _timer = new Timer(Poll, null, 200, 200); }
            Release();
            if (!File.Exists(source.Url) && source.Headers.Count > 0) throw new NotSupportedException("BASS: cabeçalhos personalizados não suportados. Selecione libmpv ou VLC.");
            _stream = File.Exists(source.Url) ? BASS_StreamCreateFile(false, source.Url, 0, 0, 0x80000000) : BASS_StreamCreateURL(source.Url, 0, 0x80000000, IntPtr.Zero, IntPtr.Zero);
            if (_stream == 0) throw new IOException("BASS não abriu áudio; código " + BASS_ErrorGetCode() + ". Formato pode exigir plugin não incluído.");
            try { cancellationToken.ThrowIfCancellationRequested(); Check(BASS_ChannelSetAttribute(_stream, 2, (float)_volume)); Check(BASS_ChannelPlay(_stream, false)); PlaybackSource = source; _started = true; }
            catch { Release(); throw; }
        }
    }, cancellationToken);
    private void Poll(object? state)
    {
        bool end = false, fail = false;
        lock (_gate)
        {
            if (_disposed || !_started || _stream == 0 || BASS_ChannelIsActive(_stream) != 0) return;
            _started = false; var length = BASS_ChannelGetLength(_stream, 0); var position = BASS_ChannelGetPosition(_stream, 0);
            end = length != ulong.MaxValue && position >= length; fail = !end;
        }
        if (end) Ended?.Invoke(this, EventArgs.Empty);
        if (fail) Failed?.Invoke(this, "BASS interrompeu áudio antes do fim.");
    }
    private static void Check(bool ok) { if (!ok) throw new IOException("BASS: código " + BASS_ErrorGetCode()); }
    public void Pause() { lock (_gate) { if (_stream != 0) Check(BASS_ChannelPause(_stream)); } }
    public void Resume() { lock (_gate) { if (_stream != 0) Check(BASS_ChannelPlay(_stream, false)); } }
    public void Seek(double seconds) { if (double.IsFinite(seconds)) lock (_gate) { if (_stream != 0) Check(BASS_ChannelSetPosition(_stream, BASS_ChannelSeconds2Bytes(_stream, Math.Max(0, seconds)), 0)); } }
    private void Release() { _started = false; if (_stream != 0) BASS_StreamFree(_stream); _stream = 0; }
    public void Dispose() { lock (_gate) { if (_disposed) return; _disposed = true; _timer?.Dispose(); Release(); if (_initialized) BASS_Free(); } }
    [DllImport("bass.dll")] private static extern bool BASS_Init(int device, uint frequency, uint flags, IntPtr window, IntPtr clsid);
    [DllImport("bass.dll")] private static extern bool BASS_Free();
    [DllImport("bass.dll")] private static extern int BASS_ErrorGetCode();
    [DllImport("bass.dll", CharSet = CharSet.Unicode)] private static extern uint BASS_StreamCreateFile(bool memory, string file, ulong offset, ulong length, uint flags);
    [DllImport("bass.dll", CharSet = CharSet.Unicode)] private static extern uint BASS_StreamCreateURL(string url, uint offset, uint flags, IntPtr proc, IntPtr user);
    [DllImport("bass.dll")] private static extern bool BASS_StreamFree(uint handle);
    [DllImport("bass.dll")] private static extern uint BASS_ChannelIsActive(uint handle);
    [DllImport("bass.dll")] private static extern bool BASS_ChannelPlay(uint handle, bool restart);
    [DllImport("bass.dll")] private static extern bool BASS_ChannelPause(uint handle);
    [DllImport("bass.dll")] private static extern bool BASS_ChannelSetAttribute(uint handle, uint attribute, float value);
    [DllImport("bass.dll")] private static extern ulong BASS_ChannelGetPosition(uint handle, uint mode);
    [DllImport("bass.dll")] private static extern ulong BASS_ChannelGetLength(uint handle, uint mode);
    [DllImport("bass.dll")] private static extern double BASS_ChannelBytes2Seconds(uint handle, ulong position);
    [DllImport("bass.dll")] private static extern ulong BASS_ChannelSeconds2Bytes(uint handle, double position);
    [DllImport("bass.dll")] private static extern bool BASS_ChannelSetPosition(uint handle, ulong position, uint mode);
}
