using System.Globalization;
using System.Text.RegularExpressions;

namespace Hibiki.Core.Models;

public static partial class TrackTextFormatter
{
    [GeneratedRegex(@"\s*[\(\[]\s*(?:official(?:\s+music)?\s+video|copyright\s+free|lyrics)\s*[\)\]]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Noise();
    [GeneratedRegex(@"\s*-\s*Topic\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Topic();

    public static string Artist(string? artist) => Topic().Replace(artist ?? "", "").Trim();

    public static string Title(string? title, string? artist)
    {
        var result = Noise().Replace(title ?? "", "").Trim();
        var name = Artist(artist);
        if (name.Length == 0) return result;
        foreach (var separator in new[] { " - ", " – ", " — " })
        {
            var prefix = name + separator;
            if (result.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && result.Length > prefix.Length)
                return result[prefix.Length..].Trim();
        }
        return result;
    }

    // Catalog and decoder commonly differ by fractions of a second; round both identically.
    public static string Duration(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) seconds = 0;
        var total = (long)Math.Min(Math.Round(seconds, MidpointRounding.AwayFromZero), long.MaxValue / 2d);
        return total >= 3600
            ? string.Create(CultureInfo.InvariantCulture, $"{total / 3600}:{total / 60 % 60:00}:{total % 60:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{total / 60}:{total % 60:00}");
    }
}
