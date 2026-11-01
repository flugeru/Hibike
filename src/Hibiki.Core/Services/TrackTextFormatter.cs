using System.Text.RegularExpressions;

namespace Hibiki.Core.Services;

/// <summary>
/// Display-only cleanup of metadata supplied by YouTube. Original Track fields
/// remain untouched for search, caching, playback and favorite persistence.
/// </summary>
public static partial class TrackTextFormatter
{
    [GeneratedRegex(@"\s*(?:\((?:official\s+(?:music\s+)?video|official\s+audio|lyrics?|lyric\s+video)\)|\[(?:copyright\s+free)\])\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VideoNoise();

    [GeneratedRegex(@"\s*[-–—]\s*Topic\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TopicSuffix();

    public static string Artist(string? artist)
    {
        if (string.IsNullOrWhiteSpace(artist)) return "Artista desconhecido";
        return TopicSuffix().Replace(artist.Trim(), "").Trim();
    }

    public static string Title(string? title, string? artist)
    {
        if (string.IsNullOrWhiteSpace(title)) return "Sem título";
        var result = title.Trim();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var clean = VideoNoise().Replace(result, "").TrimEnd();
            if (clean == result) break;
            result = clean;
        }

        var name = Artist(artist);
        if (name != "Artista desconhecido"
            && result.StartsWith(name, StringComparison.OrdinalIgnoreCase)
            && result.Length > name.Length)
        {
            var tail = result.AsSpan(name.Length).TrimStart();
            if (!tail.IsEmpty && tail[0] is '-' or '–' or '—' or ':')
            {
                var withoutPrefix = tail[1..].TrimStart().ToString();
                if (!string.IsNullOrWhiteSpace(withoutPrefix)) result = withoutPrefix;
            }
        }
        return string.IsNullOrWhiteSpace(result) ? title.Trim() : result;
    }

    // Round once, consistently, for list, mini-player, player duration and elapsed time.
    // Never use TimeSpan.FromSeconds fractional truncation on one screen and rounding on another.
    public static string Duration(double seconds, bool unknownAsDash = false)
    {
        if (!double.IsFinite(seconds) || seconds < 0 || (unknownAsDash && seconds == 0))
            return unknownAsDash ? "—" : "0:00";
        var total = (long)Math.Round(seconds, MidpointRounding.AwayFromZero);
        var minutes = total / 60;
        var remaining = total % 60;
        if (minutes < 60) return $"{minutes}:{remaining:00}";
        return $"{minutes / 60}:{minutes % 60:00}:{remaining:00}";
    }
}
