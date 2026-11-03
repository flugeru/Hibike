namespace Hibike.Core.Services;

// Snapshots are owned by the caller until Enqueue, then are read only by one worker.
public sealed class LatestSnapshotWriter<T>(Action<T> write, Action<Exception> onError) where T : class
{
    private readonly object _gate = new();
    private T? _pending;
    private Task? _worker;
    private Exception? _error;

    public void Enqueue(T snapshot)
    {
        lock (_gate)
        {
            _pending = snapshot;
            _worker ??= Task.Run(DrainAsync);
        }
    }

    private async Task DrainAsync()
    {
        while (true)
        {
            // Group adjacent status/queue changes without delaying interaction.
            await Task.Delay(50).ConfigureAwait(false);
            T snapshot;
            lock (_gate)
            {
                if (_pending is null) { _worker = null; return; }
                snapshot = _pending; _pending = null;
            }
            try { write(snapshot); lock (_gate) _error = null; }
            catch (Exception error) when (error is not OutOfMemoryException)
            { lock (_gate) _error = error; onError(error); }
        }
    }

    public async Task FlushAsync()
    {
        while (true)
        {
            Task? worker;
            lock (_gate)
            {
                worker = _worker;
                if (worker is null)
                {
                    if (_error is { } error) throw new IOException("Não foi possível salvar os dados locais.", error);
                    return;
                }
            }
            await worker.ConfigureAwait(false);
        }
    }
}
