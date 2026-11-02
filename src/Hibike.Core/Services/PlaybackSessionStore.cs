using System.Text.Json;
using Hibike.Core.Models;

namespace Hibike.Core.Services;

public sealed record PlaybackSession(Track[] Queue, Track? Current, int Index, double Position, Track[]? History = null);

public sealed class PlaybackSessionStore(string directory)
{
    private string PathName => Path.Combine(directory, "playback-session.json");
    public PlaybackSession? Load()
    {
        if (!File.Exists(PathName)) return null;
        var session = JsonSerializer.Deserialize<PlaybackSession>(File.ReadAllText(PathName));
        if (session is null || session.Queue is null || session.Queue.Any(t => t is null || string.IsNullOrWhiteSpace(t.Source))
            || session.History is { } history && (history.Length > 100 || history.Any(t => t is null || string.IsNullOrWhiteSpace(t.Source)))
            || !double.IsFinite(session.Position) || session.Position < 0) throw new InvalidDataException("Sessão de reprodução inválida.");
        return session;
    }
    public void Save(PlaybackSession session)
    {
        Directory.CreateDirectory(directory);
        var temp = PathName + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(session));
            File.Move(temp, PathName, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
