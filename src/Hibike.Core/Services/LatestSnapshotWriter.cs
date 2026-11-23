namespace Hibike.Core.Services;

// Snapshots are owned by the caller until Enqueue, then are read only by one worker.
public sealed class LatestSnapshotWriter<T>(Action<T> write, Action<Exception> onError) where T : class
{
    private static readonly TimeSpan CoalesceDelay = TimeSpan.FromMilliseconds(50);
    private readonly object _gate = new();
    private T? _pending;
    private Task? _worker;
    private Exception? _error;
    // Completed by a flush so a waiting worker writes now instead of after the coalescing delay.
    private TaskCompletionSource _wake = new(TaskCreationOptions.RunContinuationsAsynchronously);

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
            Task wake;
            lock (_gate) wake = _wake.Task;
            // Group adjacent status/queue changes without delaying interaction or shutdown.
            await Task.WhenAny(Task.Delay(CoalesceDelay), wake).ConfigureAwait(false);
            T snapshot;
            lock (_gate)
            {
                if (_wake.Task.IsCompleted) _wake = new(TaskCreationOptions.RunContinuationsAsynchronously);
                if (_pending is null) { _worker = null; return; }
                snapshot = _pending; _pending = null;
            }
            try { write(snapshot); lock (_gate) _error = null; }
            catch (Exception error) when (error is not OutOfMemoryException)
            { lock (_gate) _error = error; onError(error); }
            // Nothing arrived during the write: finish now rather than after another idle delay,
            // so a waiting flush returns as soon as the latest snapshot is durable.
            lock (_gate) if (_pending is null) { _worker = null; return; }
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
                _wake.TrySetResult();
            }
            await worker.ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Blocks until queued snapshots are written, for process shutdown where an unobserved
    /// asynchronous flush would be abandoned. Returns false when the timeout elapses first.
    /// Safe on a UI thread: the worker never resumes on the caller's synchronization context.
    /// </summary>
    public bool Flush(TimeSpan timeout)
    {
        try { return FlushAsync().Wait(timeout); }
        catch (AggregateException error) when (error.InnerException is IOException io)
        { throw io; }
    }
}
