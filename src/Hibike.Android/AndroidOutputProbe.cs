using Android.Content;
using Android.Media;
using AndroidX.Media3.ExoPlayer;
using AndroidX.Media3.ExoPlayer.Audio;
using Hibike.Core.Models;
using MediaAttributes = AndroidX.Media3.Common.AudioAttributes;

namespace Hibike.Android;

// Media3 still creates, configures and releases the AudioTrack. Observe that track only;
// connected devices and the preferred route can both differ from the route used for playback.
internal sealed class AndroidOutputProbe : Java.Lang.Object, DefaultAudioSink.IAudioTrackProvider
{
    internal sealed class Renderers(Context context, AndroidOutputProbe probe) : DefaultRenderersFactory(context)
    {
        protected override IAudioSink? BuildAudioSink(Context? context, bool enableFloatOutput, bool enableAudioTrackPlaybackParams) =>
            new DefaultAudioSink.Builder(context).SetEnableFloatOutput(enableFloatOutput)!
                .SetEnableAudioTrackPlaybackParams(enableAudioTrackPlaybackParams)!.SetAudioTrackProvider(probe)!.Build();
    }

    private volatile Observation? _current;

    public AudioTrack? GetAudioTrack(AudioSinkAudioTrackConfig? config, MediaAttributes? attributes, int sessionId, Context? context)
    {
        var track = DefaultAudioSink.IAudioTrackProvider.Default!.GetAudioTrack(config, attributes, sessionId, context);
        _current = track is null ? null : new Observation(track);
        return track;
    }

    public int GetAudioTrackChannelConfig(int channelCount) =>
        DefaultAudioSink.IAudioTrackProvider.Default!.GetAudioTrackChannelConfig(channelCount);

    internal AudioOutputInfo Snapshot() => _current?.Read() ?? new();

    private sealed class Observation(AudioTrack track)
    {
        private sealed record Sample(AudioOutputInfo Info, long Time);
        private volatile Sample? _last;
        private int _reading;

        internal AudioOutputInfo Read()
        {
            // Even getRoutedDevice may call the system audio service. Never wait on it on the UI thread.
            if (Interlocked.CompareExchange(ref _reading, 1, 0) == 0)
                _ = Task.Run(() =>
                {
                    try { _last = new(Inspect(track), Environment.TickCount64); }
                    catch (Exception e) when (e is not OutOfMemoryException) { _last = null; } // May race track release.
                    finally { Volatile.Write(ref _reading, 0); }
                });
            var sample = _last;
            return sample is not null && Environment.TickCount64 - sample.Time < 5000 ? sample.Info : new();
        }
    }

    private static AudioOutputInfo Inspect(AudioTrack track)
    {
        if (track.PlayState != PlayState.Playing) return new();
        var devices = OperatingSystem.IsAndroidVersionAtLeast(36) ? track.RoutedDevices
            : track.RoutedDevice is { } device ? [device] : [];
        var routes = devices?.Select(d => Describe(d.Type, d.ProductName)).ToArray();
        // These switches compare inlined enum constants; they do not call newer Android APIs.
#pragma warning disable CA1416
        var encoding = track.AudioFormat switch
        {
            Encoding.Pcm8bit => "PCM 8 bits", Encoding.Pcm16bit => "PCM 16 bits",
            Encoding.Pcm24bitPacked => "PCM 24 bits", Encoding.Pcm32bit => "PCM 32 bits",
            Encoding.PcmFloat => "PCM float", _ => null
        };
#pragma warning restore CA1416
        var rate = track.SampleRate;
        var channels = track.ChannelCount;
        var format = Join([encoding, rate > 0 ? $"{rate} Hz" : null, channels switch { 1 => "1 canal", > 1 => $"{channels} canais", _ => null }], " · ");
        var model = global::Android.OS.Build.Model?.Trim();
        var host = string.IsNullOrEmpty(model) || model.Equals("unknown", StringComparison.OrdinalIgnoreCase) ? "Android" : model;
        return new(Device: Join(routes?.Select(r => r.Device), " + "), Output: $"{host} · AudioTrack",
            Connection: Join(routes?.Select(r => r.Connection), " + "), Format: format,
            Note: "O formato é o enviado pelo Hibike ao Android, não necessariamente o do dispositivo final.");
    }

    private static string? Join(IEnumerable<string?>? values, string separator) =>
        string.Join(separator, (values ?? []).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct()) is { Length: > 0 } result ? result : null;

    private static (string? Device, string? Connection) Describe(AudioDeviceType type, string? product)
    {
#pragma warning disable CA1416 // Only compares inlined AudioDeviceType constants.
        (string? kind, string? connection) = type switch
        {
            AudioDeviceType.BuiltinSpeaker or AudioDeviceType.BuiltinSpeakerSafe => ("Alto-falante", "Integrada"),
            AudioDeviceType.BuiltinEarpiece => ("Auricular", "Integrada"),
            AudioDeviceType.WiredHeadset or AudioDeviceType.WiredHeadphones => ("Fones de ouvido", "Com fio"),
            AudioDeviceType.BluetoothA2dp => (null, "Bluetooth A2DP"),
            AudioDeviceType.BluetoothSco => (null, "Bluetooth SCO"),
            AudioDeviceType.BleHeadset or AudioDeviceType.BleSpeaker or AudioDeviceType.BleBroadcast => (null, "Bluetooth LE"),
            AudioDeviceType.HearingAid => ("Aparelho auditivo", null),
            AudioDeviceType.UsbDevice or AudioDeviceType.UsbAccessory or AudioDeviceType.UsbHeadset => (null, "USB"),
            AudioDeviceType.Hdmi => (null, "HDMI"), AudioDeviceType.HdmiArc => (null, "HDMI ARC"),
            AudioDeviceType.HdmiEarc => (null, "HDMI eARC"), AudioDeviceType.Ip => (null, "IP"),
            AudioDeviceType.LineAnalog or AudioDeviceType.AuxLine => (null, "Linha analógica"),
            AudioDeviceType.LineDigital => (null, "Linha digital"), _ => (null, null)
        };
#pragma warning restore CA1416
        // Android may substitute the phone's model for a missing peripheral name.
        if (string.IsNullOrWhiteSpace(product) || product == global::Android.OS.Build.Model) product = null;
        return (product is not null && kind is not null ? $"{kind} · {product}" : product ?? kind, connection);
    }
}
