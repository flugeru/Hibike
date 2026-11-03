using Hibike.Core.Models;
using Hibike.Core.Services;
using Equalizer = Android.Media.Audiofx.Equalizer;

namespace Hibike.Android;

public sealed partial class PlaybackService
{
    private void RefreshEqualizer()
    {
        var session = _player?.AudioSessionId ?? 0;
        if (_equalizerPreset == EqualizerPreset.Off || session <= 0 || session == _equalizerSession) return;
        ReleaseEqualizer();
        _equalizerSession = session;
        try
        {
            _equalizer = new Equalizer(0, session);
            var range = _equalizer.GetBandLevelRange();
            if (range is not { Length: 2 }) return;
            for (short band = 0; band < _equalizer.NumberOfBands; band++)
            {
                var hz = _equalizer.GetCenterFreq(band) / 1000;
                var gain = _equalizerPreset switch
                {
                    EqualizerPreset.Bass when hz <= 250 => 300,
                    EqualizerPreset.Voice when hz is >= 400 and <= 4000 => 240,
                    EqualizerPreset.Treble when hz >= 3500 => 300,
                    _ => 0
                };
                _equalizer.SetBandLevel(band, (short)Math.Clamp(gain, range[0], range[1]));
            }
            _equalizer.SetEnabled(true);
        }
        catch (Java.Lang.RuntimeException)
        {
            _equalizer?.Dispose();
            _equalizer = null;
            PlaybackDiagnostics.Write("Áudio", "Equalizador indisponível para a sessão de áudio atual.");
        }
    }

    private void ReleaseEqualizer()
    {
        _equalizerSession = 0;
        _equalizer?.Release();
        _equalizer?.Dispose();
        _equalizer = null;
    }
}
