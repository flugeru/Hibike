using System.Text.RegularExpressions;
namespace Hibiki.Core.Services;

public static partial class PlaybackDiagnostics
{
    private static readonly Queue<string> Lines = new();
    private static readonly object Gate = new();
    public static void Write(string stage, string message)
    {
        // Never retain signed stream URLs, cookies or authorization headers.
        message = UrlPattern().Replace(message, "[URL]");
        var text = $"{DateTime.Now:HH:mm:ss} [{stage}] {message.Trim()}";
        lock (Gate) { Lines.Enqueue(text); while (Lines.Count > 80) Lines.Dequeue(); }
    }
    public static string Snapshot() { lock (Gate) return string.Join(Environment.NewLine, Lines); }
    [GeneratedRegex(@"https?://[^\s""<>]+")]
    private static partial Regex UrlPattern();
}
