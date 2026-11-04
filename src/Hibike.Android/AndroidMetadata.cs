using Android.Media;
using AudioSource = Hibike.Core.Models.AudioSource;
using Hibike.Core.Models;
using Hibike.Core.Services;

namespace Hibike.Android;

internal static class AndroidMetadata
{
    internal static Track Read(Track track, string cache)
    {
        var result = LocalTrackMetadata.Read(track, cache);
        if (!track.IsLocal || !File.Exists(track.Source)) return result;
        try
        {
            using var reader = new MediaMetadataRetriever(); reader.SetDataSource(track.Source);
            string Value(MetadataKey key, string fallback) => reader.ExtractMetadata(key)?.Trim() is { Length: > 0 } text ? text : fallback;
            result = result with
            {
                Title = Value(MetadataKey.Title, result.Title), Artist = Value(MetadataKey.Artist, result.Artist),
                Album = Value(MetadataKey.Album, result.Album ?? ""), TrackNumber = Value(MetadataKey.CdTrackNumber, result.TrackNumber ?? ""),
                Date = Value(MetadataKey.Date, result.Date ?? "")
            };
            if (long.TryParse(reader.ExtractMetadata(MetadataKey.Duration), out var ms) && ms > 0) result = result with { DurationSeconds = ms / 1000d };
            if (result.Thumbnail is null && reader.GetEmbeddedPicture() is { Length: > 0 and <= 16777216 } picture)
            {
                Directory.CreateDirectory(cache);
                var path = Path.Combine(cache, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(picture)) + ".img");
                var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try { File.WriteAllBytes(temp, picture); File.Move(temp, path, true); }
                finally { if (File.Exists(temp)) File.Delete(temp); }
                result = result with { Thumbnail = new Uri(path).AbsoluteUri };
            }
        }
        catch (Exception) { PlaybackDiagnostics.Write("Metadados Android", "Leitura nativa indisponível; dados existentes preservados."); }
        return result;
    }
    internal static AudioSource ReadAudioSource(AudioSource source)
    {
        var result = LocalTrackMetadata.ReadAudioSource(source);
        if (!Path.IsPathFullyQualified(source.Url) || !File.Exists(source.Url)) return result;
        try
        {
        using var extractor = new MediaExtractor(); extractor.SetDataSource(source.Url);
        for (var i = 0; i < extractor.TrackCount; i++)
        {
            using var format = extractor.GetTrackFormat(i);
            var mime = format.GetString(MediaFormat.KeyMime);
            if (mime?.StartsWith("audio/", StringComparison.Ordinal) != true) continue;
            int? Value(string key) => format.ContainsKey(key) && format.GetInteger(key) is > 0 and var value ? value : null;
            result = result with
            {
                Codec = result.Codec ?? (mime == "audio/raw" ? "pcm" : mime[6..]),
                SampleRateHz = result.SampleRateHz ?? Value("sample-rate"),
                Channels = result.Channels ?? Value("channel-count"),
                BitrateKbps = result.BitrateKbps ?? (Value("bitrate") / 1000d),
                BitDepth = result.BitDepth ?? Value("bits-per-sample")
            };
            break;
        }
        }
        catch (Exception) { PlaybackDiagnostics.Write("Áudio Android", "Dados técnicos nativos indisponíveis; dados existentes preservados."); }
        return result;
    }
    internal static Task<IReadOnlyDictionary<string, string>> DetailsAsync(string path) => Task.Run<IReadOnlyDictionary<string, string>>(() =>
    {
        var result = new Dictionary<string, string>();
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path)) return result;
        var source = ReadAudioSource(new AudioSource(path, new Dictionary<string, string>()));
        if (source.Codec is { } codec) result["Codec · Android"] = codec.ToUpperInvariant();
        if (source.SampleRateHz is > 0) result["Amostragem · Android"] = $"{source.SampleRateHz / 1000:0.#} kHz";
        if (source.Channels is > 0) result["Canais · Android"] = $"{source.Channels}";
        if (source.BitDepth is > 0) result["Profundidade · Android"] = $"{source.BitDepth} bits";
        if (source.BitrateKbps is > 0) result["Bitrate · Android"] = $"{source.BitrateKbps:0.#} kb/s";
        return result;
    });
}
