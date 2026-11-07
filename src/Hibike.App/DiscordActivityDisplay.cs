using System.Text;
using Hibike.Core.Models;

namespace Hibike.App;

/// <summary>
/// Shared activity text and publicly hosted PNG for desktop RPC and Android Gateway.
/// The icon matches src/Hibike.App/Assets/hibike-desktop.png in this repository.
/// </summary>
public static class DiscordActivityDisplay
{
    public const string IconUrl = "https://raw.githubusercontent.com/flugeru/Hibike/main/site/Assets/hibike.png";

    public static (string Name, string Details, string State, string ArtworkText) Format(Track track, bool titleAsName)
    {
        var title = track.IsLocal ? track.Title : TrackTextFormatter.Title(track.Title, track.Artist);
        var artist = track.IsLocal ? track.Artist : TrackTextFormatter.Artist(track.Artist);
        if (string.IsNullOrWhiteSpace(title)) title = "Faixa desconhecida";
        if (string.IsNullOrWhiteSpace(artist)) artist = "Artista desconhecido";
        var album = string.IsNullOrWhiteSpace(track.Album) ? null : track.Album.Trim();

        return (Limit(titleAsName ? title : "Hibike"),
            Limit(titleAsName ? artist : title),
            Limit(titleAsName ? album ?? "Álbum não informado" : artist),
            Limit(album ?? title));
    }

    public static string Limit(string value)
    {
        var result = new StringBuilder();
        var bytes = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            if (bytes + rune.Utf8SequenceLength > 128) break;
            result.Append(rune);
            bytes += rune.Utf8SequenceLength;
        }
        return result.Length == 0 ? "—" : result.ToString();
    }
}
