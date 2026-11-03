namespace Hibike.Core.Models;

public sealed record Track(string Id, string Title, string Artist, string Source,
    double DurationSeconds = 0, string? Thumbnail = null, bool IsLocal = false, string? Album = null, string? TrackNumber = null, string? Date = null,
    string? MusicTitle = null, string? MusicArtist = null, string[]? EquivalentVideoIds = null, string? ArtistId = null, string? AlbumId = null, double? ReplayGainDb = null)
{
    public string DurationLabel => DurationSeconds > 0 ? TrackTextFormatter.Duration(DurationSeconds) : "—";
}

public enum EqualizerPreset { Off, Bass, Voice, Treble }

public sealed record AudioSource(string Url, IReadOnlyDictionary<string, string> Headers, string? Title = null, string? Artist = null, string? Artwork = null,
    double? BitrateKbps = null, string? Codec = null, double? SampleRateHz = null, double? Channels = null, string? Format = null, string? Container = null, int? BitDepth = null, double? GainDb = null, EqualizerPreset Equalizer = EqualizerPreset.Off);
public sealed record ProcessResult(int ExitCode, string Output, string Error);
