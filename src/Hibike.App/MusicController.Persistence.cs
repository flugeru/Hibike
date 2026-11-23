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
        Theme = _personal.Theme,
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

    public async Task FlushPendingSavesAsync()
    {
        try { await _volumeWriter.FlushAsync().ConfigureAwait(false); }
        catch (IOException error) { PlaybackDiagnostics.Write("Salvar volume", error.Message); }
        await _stateWriter.FlushAsync().ConfigureAwait(false);
    }
    public void FlushPendingSaves()
    {
        // Only lifecycle shutdown waits; ordinary UI changes never wait for storage.
        // The process may exit right after this call, so an unobserved async flush would lose
        // the final queue, position, preferences and volume.
        // Each writer is flushed independently so one failing file never skips the other.
        Flush(_stateWriter, "Salvar sessão");
        Flush(_volumeWriter, "Salvar volume");
        static void Flush<T>(LatestSnapshotWriter<T> writer, string stage) where T : class
        {
            try
            {
                if (!writer.Flush(ShutdownFlushTimeout))
                    PlaybackDiagnostics.Write(stage, "Tempo limite ao salvar antes de encerrar.");
            }
            catch (IOException error) { PlaybackDiagnostics.Write(stage, error.Message); }
        }
    }
    private static readonly TimeSpan ShutdownFlushTimeout = TimeSpan.FromSeconds(3);
}
