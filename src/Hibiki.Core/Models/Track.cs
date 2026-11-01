namespace Hibiki.Core.Models;

public sealed record Track(string Id, string Title, string Artist, string Source,
    double DurationSeconds = 0, string? Thumbnail = null, bool IsLocal = false)
{
    public string DurationLabel => DurationSeconds > 0
        ? TimeSpan.FromSeconds(DurationSeconds).ToString(DurationSeconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss") : "—";
}

public sealed record AudioSource(string Url, IReadOnlyDictionary<string, string> Headers, string? Title = null, string? Artist = null, string? Artwork = null);
public sealed record ProcessResult(int ExitCode, string Output, string Error);
