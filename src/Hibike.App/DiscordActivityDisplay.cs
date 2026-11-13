using System.Text;
using Hibike.Core.Models;

namespace Hibike.App;

/// <summary>
/// Shared activity text and publicly hosted PNG for desktop RPC and Android Gateway.
/// The icon matches src/Hibike.App/Assets/hibike-desktop.png in this repository.
/// </summary>
public static class DiscordActivityDisplay
{
    public const string AppName = "Hibike";
    public const string IconUrl = "https://raw.githubusercontent.com/flugeru/Hibike/main/site/Assets/hibike.png";

    public static (string Name, string? Details, string State, string? ArtworkText) Format(Track track)
    {
        var title = Optional(track.IsLocal ? track.Title : TrackTextFormatter.Title(track.Title, track.Artist));
        var artist = track.IsLocal ? track.Artist : TrackTextFormatter.Artist(track.Artist);
        var album = track.Album?.Trim();
        if (string.IsNullOrWhiteSpace(album)
            || string.Equals(album.Normalize(), track.Artist?.Trim().Normalize(), StringComparison.OrdinalIgnoreCase)
            || string.Equals(album.Normalize(), artist?.Trim().Normalize(), StringComparison.OrdinalIgnoreCase))
            album = null;

        // The card has one secondary text field. Reserve space for Hibike even on long albums.
        var state = album is null ? AppName : Limit(album, 128 - Encoding.UTF8.GetByteCount(" · " + AppName)) + " · " + AppName;
        return (title ?? AppName, Optional(artist), state, Optional(album));
    }

    private static string? Optional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : Limit(value.Trim());

    private static string Limit(string value, int maxBytes = 128)
    {
        var result = new StringBuilder();
        var bytes = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            if (bytes + rune.Utf8SequenceLength > maxBytes) break;
            result.Append(rune);
            bytes += rune.Utf8SequenceLength;
        }
        return result.Length == 0 ? "—" : result.ToString();
    }
}
