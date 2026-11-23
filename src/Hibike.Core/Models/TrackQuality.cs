namespace Hibike.Core.Models;

public static class TrackQuality
{
    // Classify the encoding of the selected source, not the master or the listening device.
    public static string Describe(AudioSource? source)
    {
        var codec = source?.Codec?.Trim().ToLowerInvariant();
        if (codec?.StartsWith("audio/", StringComparison.Ordinal) == true) codec = codec[6..];
        return codec switch
        {
            "flac" or "x-flac" or "alac" or "pcm" or "raw" or "pcm_s16le" or "pcm_s16be" or
            "pcm_s24le" or "pcm_s24be" or "pcm_s32le" or "pcm_s32be" or "pcm_f32le" or "pcm_f64le"
                => source is { BitDepth: >= 24, SampleRateHz: > 48000 } ? "Alta resolução · sem perdas" : "Sem perdas",
            "mpeg" or "mpga" or "mp3" or "aac" or "mp4a-latm" or "mp4a.40.2" or "mp4a.40.5" or
            "mp4a.40.29" or "opus" or "vorbis" or "ac3" or "eac3"
                => "Compressão com perdas",
            _ => "Qualidade não informada"
        };
    }
}
