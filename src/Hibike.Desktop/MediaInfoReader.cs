using System.Diagnostics;
using System.Text.Json;

namespace Hibike.Desktop;

internal static class MediaInfoReader
{
    public static async Task<IReadOnlyDictionary<string, string>> ReadAsync(string path)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.IsFile) path = uri.LocalPath;
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path)) return new Dictionary<string, string>();
        var bundled = Path.Combine(AppContext.BaseDirectory, "MediaInfo.exe");
        var start = new ProcessStartInfo(File.Exists(bundled) ? bundled : "mediainfo")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("--Output=JSON"); start.ArgumentList.Add(path);
        using var process = Process.Start(start) ?? throw new IOException("MediaInfo não iniciou.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var json = await output; await error;
            if (process.ExitCode != 0) return new Dictionary<string, string>();
            return Parse(json);
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }
    internal static IReadOnlyDictionary<string, string> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var result = new Dictionary<string, string>();
        foreach (var track in document.RootElement.GetProperty("media").GetProperty("track").EnumerateArray())
        {
            if (!track.TryGetProperty("@type", out var type) || type.GetString() != "Audio") continue;
            foreach (var (key, label, suffix) in new[] { ("Format", "Codec · MediaInfo", ""), ("BitRate", "Bitrate · MediaInfo", " b/s"), ("SamplingRate", "Amostragem · MediaInfo", " Hz"), ("Channels", "Canais · MediaInfo", ""), ("BitDepth", "Profundidade · MediaInfo", " bits"), ("Title", "Título · MediaInfo", ""), ("Performer", "Artista · MediaInfo", "") })
                if (track.TryGetProperty(key, out var value) && !string.IsNullOrWhiteSpace(value.ToString())) result[label] = value + suffix;
            break;
        }
        return result;
    }
}
