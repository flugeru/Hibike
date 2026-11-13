using System.Text.Json;
using Hibike.Core.Services;

namespace Hibike.App;

public sealed partial class MusicController
{
    private sealed record PersistedState(PlaybackSession? Session, Personalization Personal);
    private readonly LatestSnapshotWriter<PersistedState> _stateWriter;
    private bool _notifyingSaveError;

    private void ReportSaveError(Exception error)
    {
        PlaybackDiagnostics.Write("Salvar sessão", error.Message);
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _notifyingSaveError = true;
            try { HasError = true; Status = "Não foi possível salvar os dados locais: " + error.Message; Changed?.Invoke(); }
            finally { _notifyingSaveError = false; }
        });
    }

    private Personalization PersonalSnapshot() => new()
    {
        Autoplay = _personal.Autoplay, ReduceMotion = _personal.ReduceMotion, OfflineMode = _personal.OfflineMode, Discovery = _personal.Discovery,
        OnboardingCompleted = _personal.OnboardingCompleted, Region = _personal.Region, Weights = _personal.Weights,
        Pins = [.. _personal.Pins], Artists = [.. _personal.Artists], Genres = [.. _personal.Genres],
        Dismissed = new(_personal.Dismissed),
        Playlists = _personal.Playlists.Select(p => new LocalPlaylist { Name = p.Name, Pinned = p.Pinned, Tracks = [.. p.Tracks] }).ToList(),
        Listening = _personal.Listening.ToDictionary(p => p.Key, p => new ListeningStat
        { Seconds = p.Value.Seconds, Plays = p.Value.Plays, Completed = p.Value.Completed, Skips = p.Value.Skips, LastPlayed = p.Value.LastPlayed })
    };

    private void QueueStateSave()
    {
        // Copy mutable data on its owning UI thread. Serialization and all writes stay on the worker.
        var session = _initialized ? new PlaybackSession(Queue.ToArray(), Current, _queueIndex,
            Math.Max(0, Position), History.Take(100).ToArray()) : null;
        _stateWriter.Enqueue(new(session, PersonalSnapshot()));
        _lastSave = DateTime.UtcNow;
    }

    private void WriteState(PersistedState snapshot)
    {
        if (snapshot.Session is { } session) _session.Save(session);
        if (_personalizationUnreadable) throw new IOException("Personalização original ilegível; arquivo preservado. Restaure um backup antes de salvar preferências.");
        Directory.CreateDirectory(DataDirectory);
        var path = Path.Combine(DataDirectory, "personalization.json");
        var temporary = path + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, snapshot.Personal);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public Task FlushPendingSavesAsync() => _stateWriter.FlushAsync();
    public void FlushPendingSaves()
    {
        // Only lifecycle shutdown/suspension waits; ordinary UI changes never wait for storage.
        try { _ = FlushPendingSavesAsync(); }
        catch (IOException error) { PlaybackDiagnostics.Write("Salvar sessão", error.Message); }
    }
}
