using System.Text.Json;
using Hibike.Core.Models;

namespace Hibike.Core.Services;

public sealed record PlaybackSession(Track[] Queue, Track? Current, int Index, double Position, Track[]? History = null);

public sealed class PlaybackSessionStore(string directory)
{
    public const int MaxQueue = 1000;
    public const int MaxHistory = 100;
    private string PathName => Path.Combine(directory, "playback-session.json");
    public PlaybackSession? Load()
    {
        if (!File.Exists(PathName)) return null;
        var session = JsonSerializer.Deserialize<PlaybackSession>(File.ReadAllText(PathName));
        if (session is null || session.Queue is null || session.Queue.Any(t => t is null || string.IsNullOrWhiteSpace(t.Source))
            || session.Current is { } current && string.IsNullOrWhiteSpace(current.Source)
            || session.Queue.Length > MaxQueue || session.Index < -1 || session.Index >= Math.Max(1, session.Queue.Length)
            || session.History is { } history && (history.Length > MaxHistory || history.Any(t => t is null || string.IsNullOrWhiteSpace(t.Source)))
            || !double.IsFinite(session.Position) || session.Position < 0) throw new InvalidDataException("Sessão de reprodução inválida.");
        return session;
    }
    public void Save(PlaybackSession session)
    {
        if (session.Queue.Length > MaxQueue || session.History is { Length: > MaxHistory })
            throw new InvalidDataException("Sessão de reprodução excede os limites.");
        Directory.CreateDirectory(directory);
        var temp = PathName + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, session);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, PathName, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
