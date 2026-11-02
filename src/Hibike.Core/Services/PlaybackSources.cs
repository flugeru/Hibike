using System.Text;
using System.Text.RegularExpressions;
using Hibike.Core.Models;

namespace Hibike.Core.Services;

public static class PlaybackSources
{
    public const string Warning = "Fontes alternativas podem trazer cover, remix ou outra gravação, mesmo com verificações. Correspondência provável, não confirmada por áudio.";
    public static string Provider(Track track)
    {
        if (track.IsLocal) return "Arquivo local";
        if (!Uri.TryCreate(track.Source, UriKind.Absolute, out var uri)) return "Fonte original";
        if (uri.Host == "youtu.be" || uri.Host == "youtube.com" || uri.Host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase)) return "YouTube";
        if (uri.Host == "soundcloud.com" || uri.Host.EndsWith(".soundcloud.com", StringComparison.OrdinalIgnoreCase)) return "SoundCloud";
        return uri.Host;
    }

    private static string Normalize(string value) => string.Concat(value.Normalize(NormalizationForm.FormKD)
        .EnumerateRunes().Where(Rune.IsLetterOrDigit).Select(r => Rune.ToLowerInvariant(r).ToString()));
    private static string Versions(Track track) => string.Join("|", Regex.Matches(track.Title.ToLowerInvariant(),
        @"\b(cover|remix|live|instrumental|karaoke|slowed|reverb|sped\s*up|nightcore|acoustic|remaster(?:ed)?|extended|edit|demo|mono|stereo)\b")
        .Select(m => m.Value).Distinct().Order());
    public static bool IsProbableMatch(Track original, Track candidate)
    {
        if (original.IsLocal || candidate.IsLocal || original.Source == candidate.Source) return false;
        var artist = Normalize(TrackTextFormatter.Artist(original.MusicArtist ?? original.Artist));
        var other = Normalize(TrackTextFormatter.Artist(candidate.MusicArtist ?? candidate.Artist));
        if (artist.Length == 0 || artist is "youtube" or "soundcloud") return false;
        // Uploaders differ between services; an explicit artist prefix in the title is useful evidence.
        bool HasArtistPrefix(Track t, string name) => Regex.Split(t.Title, @"\s+[-–—|:]\s+").Length > 1
            && Normalize(Regex.Split(t.Title, @"\s+[-–—|:]\s+")[0]) == name;
        var artistMatches = artist == other || HasArtistPrefix(candidate, artist)
            || other.Length > 0 && HasArtistPrefix(original, other);
        if (!artistMatches) return false;
        string Title(Track t)
        {
            var text = t.MusicTitle ?? t.Title;
            var parts = Regex.Split(text, @"\s+[-–—|:]\s+", RegexOptions.None);
            if (parts.Length > 1 && (Normalize(parts[0]) == artist || Normalize(parts[0]) == other))
                text = string.Join(" - ", parts.Skip(1));
            return Normalize(Regex.Replace(text, @"\b(?:official(?:\s+music)?\s+(?:video|audio)|lyrics?|hd|hq|4k)\b", "", RegexOptions.IgnoreCase));
        }
        var title = Title(original);
        return title.Length > 0 && title == Title(candidate) && Versions(original) == Versions(candidate)
            && double.IsFinite(original.DurationSeconds) && double.IsFinite(candidate.DurationSeconds)
            && original.DurationSeconds > 0 && candidate.DurationSeconds > 0
            && Math.Abs(original.DurationSeconds - candidate.DurationSeconds) <= Math.Min(8, Math.Max(4, original.DurationSeconds * .04));
    }

    public static async Task<IReadOnlyList<Track>> FindAsync(IMusicCatalog catalog, Track original, CancellationToken token)
    {
        // ponytail: searchable providers are YouTube/SoundCloud; other yt-dlp links remain original-only until a search adapter exists.
        var provider = Provider(original);
        if (original.IsLocal || provider is not ("YouTube" or "SoundCloud")) return [];
        var query = (original.MusicArtist ?? TrackTextFormatter.Artist(original.Artist)) + " "
            + (original.MusicTitle ?? TrackTextFormatter.Title(original.Title, original.Artist));
        var prefix = provider == "YouTube" ? "scsearch15:" : "ytsearch15:";
        var candidates = await catalog.SearchAsync(prefix + query, token);
        return candidates.Where(t => Provider(t) == (provider == "YouTube" ? "SoundCloud" : "YouTube") && IsProbableMatch(original, t))
            .DistinctBy(t => t.Source).Take(2).ToArray();
    }

    public static async Task<(Track Track, AudioSource Audio)> ResolveFirstAsync(IMusicCatalog catalog, Track original, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        async Task<(Track, AudioSource)> Resolve(Track track) => (track, await catalog.ResolveAsync(track, stop.Token));
        async Task<(Track, AudioSource)> Alternative()
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            foreach (var track in await FindAsync(catalog, original, timeout.Token))
            {
                try { return (track, await catalog.ResolveAsync(track, timeout.Token)); }
                catch (Exception error) when (error is not OperationCanceledException) { }
            }
            throw new InvalidOperationException("Nenhuma fonte alternativa compatível disponível.");
        }
        var pending = new List<Task<(Track, AudioSource)>> { Resolve(original) };
        if (!original.IsLocal && Provider(original) is "YouTube" or "SoundCloud") pending.Add(Alternative());
        Exception? firstError = null;
        try
        {
            while (pending.Count > 0)
            {
                var finished = await Task.WhenAny(pending).WaitAsync(token);
                pending.Remove(finished);
                try { return await finished; }
                catch (Exception error) when (!token.IsCancellationRequested) { firstError ??= error; }
            }
            throw firstError ?? new InvalidOperationException("Nenhuma fonte disponível.");
        }
        finally
        {
            stop.Cancel();
            foreach (var task in pending) _ = ObserveAsync(task);
        }
    }
    private static async Task ObserveAsync(Task task) { try { await task; } catch (Exception) { } }
}
