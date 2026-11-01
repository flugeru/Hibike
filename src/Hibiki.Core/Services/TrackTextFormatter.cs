using System.Globalization;
using System.Text.RegularExpressions;

namespace Hibiki.Core.Services;

/// <summary>Formats presentation text without changing the stored track metadata.</summary>
public static class TrackTextFormatter
{
    private static readonly Regex TopicSuffix = new(@"\s*-\s*Topic\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex NoiseSuffix = new(
        @"\s*(?:\(\s*(?:official(?:\s+music)?\s+video|lyrics)\s*\)|\[\s*copyright\s+free\s*\])\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string Artist(string? artist)
    {
        var original = artist?.Trim() ?? "";
        var cleaned = TopicSuffix.Replace(original, "").Trim();
        return cleaned.Length == 0 ? original : cleaned;
    }

    public static string Title(string? title, string? artist)
    {
        var original = title?.Trim() ?? "";
        var cleaned = original;
        string previous;
        do
        {
            previous = cleaned;
            cleaned = NoiseSuffix.Replace(cleaned, "").TrimEnd();
        } while (cleaned.Length > 0 && cleaned != previous);
        if (cleaned.Length == 0) cleaned = original;

        var name = Artist(artist);
        if (name.Length > 0 && cleaned.StartsWith(name, StringComparison.OrdinalIgnoreCase))
        {
            var rest = cleaned[name.Length..].TrimStart();
            if (rest.Length > 0 && rest[0] is '-' or '–' or '—')
            {
                var withoutArtist = rest[1..].TrimStart();
                if (withoutArtist.Length > 0) cleaned = withoutArtist;
            }
        }
        return cleaned;
    }

    public static string Duration(double seconds, bool unknownWhenZero = false)
    {
        if (!double.IsFinite(seconds) || seconds < 0 || seconds > TimeSpan.MaxValue.TotalSeconds)
            return unknownWhenZero ? "—" : "0:00";
        if (unknownWhenZero && seconds <= 0) return "—";
        var total = (long)Math.Round(seconds, MidpointRounding.AwayFromZero);
        var hours = total / 3600;
        var minutes = total / 60 % 60;
        var remainder = total % 60;
        return hours > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{hours}:{minutes:00}:{remainder:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes}:{remainder:00}");
    }
}
