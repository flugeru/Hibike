using System.Runtime.InteropServices;
using Hibike.Core.Models;
using Hibike.Core.Services;

namespace Hibike.Desktop;

public sealed class MpvAudioPlayer : IAudioPlayer
{
    private readonly object _gate = new();
    private IntPtr _handle;
    private Timer? _timer;
    private bool _disposed, _loaded;
    private double _volume = .8, _position, _duration;
    public bool IsPlaying { get; private set; }
    public double Position { get { lock (_gate) return _position; } }
    public double Duration { get { lock (_gate) return _duration; } }
    public AudioSource? PlaybackSource { get; private set; }
    public event EventHandler? Ended;
    public event EventHandler<string>? Failed;
    public double Volume { get { lock (_gate) return _volume; } set { if (double.IsFinite(value)) lock (_gate) { _volume = Math.Clamp(value, 0, 1); if (_handle != IntPtr.Zero) Set("volume", (_volume * 100).ToString(System.Globalization.CultureInfo.InvariantCulture)); } } }
    public Task PlayAsync(AudioSource source, CancellationToken cancellationToken) => Task.Run(() =>
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (_handle == IntPtr.Zero)
            {
                _handle = mpv_create();
                if (_handle == IntPtr.Zero) throw new IOException("libmpv não iniciou.");
                Check(mpv_set_option_string(_handle, "config", "no"));
                Check(mpv_set_option_string(_handle, "vid", "no"));
                Check(mpv_set_option_string(_handle, "tls-verify", "yes"));
                var certificates = Path.Combine(AppContext.BaseDirectory, "cacert.pem");
                if (File.Exists(certificates)) Check(mpv_set_option_string(_handle, "tls-ca-file", certificates));
                var ytdl = mpv_set_option_string(_handle, "ytdl", "no");
                if (ytdl != -5) Check(ytdl); // MPV_ERROR_OPTION_NOT_FOUND: builds without the ytdl hook.
                Check(mpv_initialize(_handle));
                _timer = new Timer(Poll, null, 100, 100);
            }
            Command("stop"); _loaded = false; _position = _duration = 0; IsPlaying = false;
            while (Marshal.PtrToStructure<MpvEvent>(mpv_wait_event(_handle, 0)).Id != 0) { }
            Set("http-header-fields", ""); Set("user-agent", "Hibike"); Set("referrer", "");
            var headers = new List<string>();
            foreach (var (name, value) in source.Headers)
            {
                if (string.IsNullOrWhiteSpace(name) || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-') || value.Contains('\r') || value.Contains('\n') || value.Contains('\0')) throw new InvalidDataException("Cabeçalho de áudio inválido.");
                if (name.Equals("User-Agent", StringComparison.OrdinalIgnoreCase)) Set("user-agent", value);
                else if (name.Equals("Referer", StringComparison.OrdinalIgnoreCase)) Set("referrer", value);
                else headers.Add(name + ": " + value);
            }
            SetHeaders(headers);
            Set("volume", (_volume * 100).ToString(System.Globalization.CultureInfo.InvariantCulture));
            Set("pause", "no");
            Command("loadfile", source.Url, "replace"); PlaybackSource = source;
            if (cancellationToken.IsCancellationRequested) { Command("stop"); cancellationToken.ThrowIfCancellationRequested(); }
        }
    }, cancellationToken);
    private void Poll(object? state)
    {
        bool ended = false, failed = false;
        lock (_gate)
        {
            if (_disposed || _handle == IntPtr.Zero) return;
            for (var i = 0; i < 64; i++)
            {
                var e = Marshal.PtrToStructure<MpvEvent>(mpv_wait_event(_handle, 0));
                if (e.Id == 0) break;
                if (e.Id == 8) _loaded = true;
                if (e.Id == 7 && e.Data != IntPtr.Zero)
                {
                    var reason = Marshal.ReadInt32(e.Data);
                    ended |= reason == 0; failed |= reason == 4; _loaded = false;
                }
            }
            _position = Number("time-pos"); _duration = Number("duration");
            IsPlaying = _loaded && Property("pause") == "no" && Property("paused-for-cache") != "yes";
        }
        if (ended) Ended?.Invoke(this, EventArgs.Empty);
        if (failed) Failed?.Invoke(this, "libmpv não conseguiu abrir ou continuar áudio.");
    }
    private double Number(string name) => double.TryParse(Property(name), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) ? Math.Max(0, n) : 0;
    private string? Property(string name) { var p = mpv_get_property_string(_handle, name); if (p == IntPtr.Zero) return null; try { return Marshal.PtrToStringUTF8(p); } finally { mpv_free(p); } }
    private void Set(string name, string value) => Check(mpv_set_property_string(_handle, name, value));
    private void SetHeaders(List<string> headers)
    {
        if (headers.Count == 0) return;
        var strings = headers.Select(Marshal.StringToCoTaskMemUTF8).ToArray();
        var values = Marshal.AllocHGlobal(Marshal.SizeOf<Node>() * strings.Length);
        var list = Marshal.AllocHGlobal(Marshal.SizeOf<NodeList>());
        try
        {
            for (var i = 0; i < strings.Length; i++) Marshal.StructureToPtr(new Node { Value = strings[i], Format = 1 }, values + i * Marshal.SizeOf<Node>(), false);
            Marshal.StructureToPtr(new NodeList { Count = strings.Length, Values = values }, list, false);
            var node = new Node { Value = list, Format = 7 }; Check(mpv_set_property(_handle, "http-header-fields", 6, ref node));
        }
        finally { Marshal.FreeHGlobal(list); Marshal.FreeHGlobal(values); foreach (var s in strings) Marshal.FreeCoTaskMem(s); }
    }
    private static void Check(int error) { if (error < 0) throw new IOException("libmpv: " + Marshal.PtrToStringUTF8(mpv_error_string(error))); }
    private void Command(params string[] args)
    {
        var strings = args.Select(Marshal.StringToCoTaskMemUTF8).ToArray();
        var array = Marshal.AllocHGlobal((strings.Length + 1) * IntPtr.Size);
        try { for (var i = 0; i < strings.Length; i++) Marshal.WriteIntPtr(array, i * IntPtr.Size, strings[i]); Marshal.WriteIntPtr(array, strings.Length * IntPtr.Size, IntPtr.Zero); Check(mpv_command(_handle, array)); }
        finally { Marshal.FreeHGlobal(array); foreach (var s in strings) Marshal.FreeCoTaskMem(s); }
    }
    public Task StopAsync() => Task.Run(() =>
    {
        lock (_gate)
        {
            if (_handle != IntPtr.Zero) Command("stop");
            _loaded = IsPlaying = false;
            _position = _duration = 0;
            PlaybackSource = null;
        }
    });
    public void Pause() { lock (_gate) { if (_handle != IntPtr.Zero) Set("pause", "yes"); IsPlaying = false; } }
    public void Resume() { lock (_gate) { if (_handle != IntPtr.Zero) Set("pause", "no"); } }
    public void Seek(double seconds) { if (double.IsFinite(seconds)) lock (_gate) { if (_handle != IntPtr.Zero) Command("seek", Math.Max(0, seconds).ToString(System.Globalization.CultureInfo.InvariantCulture), "absolute"); } }
    public void Dispose() { lock (_gate) { if (_disposed) return; _disposed = true; _timer?.Dispose(); if (_handle != IntPtr.Zero) mpv_terminate_destroy(_handle); _handle = IntPtr.Zero; IsPlaying = false; } }
    [StructLayout(LayoutKind.Sequential)] private struct MpvEvent { public int Id, Error; public ulong Reply; public IntPtr Data; }
    [StructLayout(LayoutKind.Sequential)] private struct Node { public IntPtr Value; public int Format; }
    [StructLayout(LayoutKind.Sequential)] private struct NodeList { public int Count; public IntPtr Values, Keys; }
    private const string Dll = "libmpv-2.dll";
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr mpv_create();
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern int mpv_initialize(IntPtr h);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern void mpv_terminate_destroy(IntPtr h);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern int mpv_command(IntPtr h, IntPtr args);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr mpv_wait_event(IntPtr h, double timeout);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr mpv_error_string(int error);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern void mpv_free(IntPtr p);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr mpv_get_property_string(IntPtr h, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern int mpv_set_property_string(IntPtr h, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern int mpv_set_property(IntPtr h, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int format, ref Node value);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern int mpv_set_option_string(IntPtr h, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
}
