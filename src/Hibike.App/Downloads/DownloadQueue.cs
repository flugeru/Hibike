using System.Collections.ObjectModel;
using System.ComponentModel;
using Hibike.Core.Models;
using Hibike.Core.Services;

namespace Hibike.App.Downloads;

internal enum TransferStage { Queued, Resolving, Transferring, Finalizing, Paused, Completed, Failed, Cancelled }

/// <summary>Observable state for a single independent download. Changes are delivered on the UI context.</summary>
internal sealed class DownloadJob(Track track) : INotifyPropertyChanged
{
    public Track Track { get; } = track;
    public TransferStage Stage { get; private set; } = TransferStage.Queued;
    public long Bytes { get; private set; }
    public long? TotalBytes { get; private set; }
    public double BytesPerSecond { get; private set; }
    public string? Error { get; private set; }
    public CancellationTokenSource? Cancellation { get; internal set; }
    public bool CancelRequested { get; internal set; }
    public bool PauseRequested { get; internal set; }

    public bool CanPause => Stage is TransferStage.Queued or TransferStage.Resolving or TransferStage.Transferring;
    public bool CanResume => (Stage is TransferStage.Paused or TransferStage.Failed) && Cancellation is null;
    public bool CanCancel => Stage is TransferStage.Queued or TransferStage.Resolving or TransferStage.Transferring
        or TransferStage.Paused or TransferStage.Failed;
    public double Progress => TotalBytes is > 0 ? Math.Clamp((double)Bytes / TotalBytes.Value, 0, 1) : 0;
    public bool HasKnownLength => TotalBytes is > 0;
    public bool IsIndeterminate => Stage == TransferStage.Transferring && !HasKnownLength;
    public string StatusText
    {
        get
        {
            var label = Stage switch
            {
                TransferStage.Queued => "Na fila",
                TransferStage.Resolving => "Obtendo áudio",
                TransferStage.Transferring => "Baixando",
                TransferStage.Finalizing => "Salvando na biblioteca",
                TransferStage.Paused => "Pausado",
                TransferStage.Completed => "Concluído",
                TransferStage.Failed => Error ?? "Falha no download",
                _ => "Cancelado"
            };
            if (Stage is not (TransferStage.Transferring or TransferStage.Paused or TransferStage.Finalizing)) return label;
            var bytes = TotalBytes is > 0
                ? $"{Bytes / 1048576d:0.0}/{TotalBytes.Value / 1048576d:0.0} MB"
                : $"{Bytes / 1048576d:0.0} MB";
            if (Stage != TransferStage.Transferring || BytesPerSecond <= 1) return $"{label} · {bytes}";
            var speed = BytesPerSecond >= 1048576 ? $"{BytesPerSecond / 1048576d:0.0} MB/s" : $"{BytesPerSecond / 1024d:0} KB/s";
            var remaining = TotalBytes is > 0
                ? $" · ETA {TimeSpan.FromSeconds(Math.Min(86400, Math.Max(0, TotalBytes.Value - Bytes) / BytesPerSecond)).ToString("c")}"
                : "";
            return $"{label} · {bytes} · {speed}{remaining}";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? Changed;

    internal void ChangeStage(TransferStage value, string? error = null)
    {
        Stage = value;
        Error = error;
        Notify();
    }

    internal void UpdateProgress(AudioDownloadProgress update)
    {
        Bytes = update.Bytes;
        TotalBytes = update.TotalBytes;
        BytesPerSecond = update.BytesPerSecond;
        Notify();
    }

    private void Notify()
    {
        foreach (var name in new[] { nameof(Stage), nameof(StatusText), nameof(Progress),
            nameof(HasKnownLength), nameof(IsIndeterminate), nameof(CanPause), nameof(CanResume), nameof(CanCancel) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        Changed?.Invoke();
    }
}

/// <summary>
/// Independent transfer coordinator; jobs survive navigation between views and use a bounded
/// number of workers. The UI provides resolution/registration delegates, not network loop logic.
/// Caller must enqueue and interact with jobs on the same UI synchronization context.
/// </summary>
internal sealed class DownloadQueue(
    string directory,
    Func<Track, CancellationToken, Task<AudioSource>> resolve,
    Func<Track, AudioSource, string, CancellationToken, Task> register,
    int maxConcurrency = 2)
{
    private readonly SemaphoreSlim _slots = new(Math.Clamp(maxConcurrency, 1, 4));
    public ObservableCollection<DownloadJob> Jobs { get; } = [];
    public event Action? Changed;

    public DownloadJob Enqueue(Track track)
    {
        var existing = Jobs.FirstOrDefault(job => job.Track.Source == track.Source
            && job.Stage is not (TransferStage.Completed or TransferStage.Cancelled));
        if (existing is not null) return existing;

        var item = new DownloadJob(track);
        item.Changed += () => Changed?.Invoke();
        Jobs.Insert(0, item);
        Changed?.Invoke();
        _ = ExecuteAsync(item);
        return item;
    }

    public void Pause(DownloadJob job)
    {
        if (!job.CanPause || job.Cancellation is not { } source) return;
        job.PauseRequested = true;
        source.Cancel();
    }

    public void PauseAll()
    {
        foreach (var job in Jobs.ToArray()) Pause(job);
    }

    public void Cancel(DownloadJob job)
    {
        if (!job.CanCancel) return;
        if (job.Cancellation is { } active)
        {
            job.CancelRequested = true;
            job.PauseRequested = false;
            active.Cancel();
            return;
        }
        AudioDownload.DiscardPartial(directory, job.Track.Source);
        job.ChangeStage(TransferStage.Cancelled);
    }

    public void Resume(DownloadJob job)
    {
        if (!job.CanResume || job.Cancellation is not null) return;
        job.CancelRequested = job.PauseRequested = false;
        job.ChangeStage(TransferStage.Queued);
        _ = ExecuteAsync(job);
    }

    public void ClearFinished()
    {
        foreach (var item in Jobs.Where(job =>
            job.Stage is TransferStage.Completed or TransferStage.Cancelled).ToArray())
            Jobs.Remove(item);
        Changed?.Invoke();
    }

    private async Task ExecuteAsync(DownloadJob job)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(30));
        job.Cancellation = cancellation;
        var acquired = false;
        string? destination = null;
        var final = TransferStage.Failed;
        string? failure = null;

        try
        {
            await _slots.WaitAsync(cancellation.Token);
            acquired = true;
            job.ChangeStage(TransferStage.Resolving);
            using var resolving = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
            resolving.CancelAfter(TimeSpan.FromSeconds(35));
            var source = await resolve(job.Track, resolving.Token);
            cancellation.Token.ThrowIfCancellationRequested();

            // The progress callback is posted to the synchronization context from which
            // the job was started, independently of the background HTTP/IO operations.
            var progress = new Progress<AudioDownloadProgress>(update => job.UpdateProgress(update));
            job.ChangeStage(TransferStage.Transferring);
            destination = await AudioDownload.SaveAsync(source, directory, progress,
                cancellation.Token, job.Track.Source);
            cancellation.Token.ThrowIfCancellationRequested();

            job.ChangeStage(TransferStage.Finalizing);
            await register(job.Track, source, destination, cancellation.Token);
            destination = null;
            final = TransferStage.Completed;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            final = job.CancelRequested ? TransferStage.Cancelled : TransferStage.Paused;
        }
        catch (OperationCanceledException)
        {
            final = TransferStage.Failed;
            failure = "Tempo esgotado ao obter o áudio";
        }
        catch (Exception error)
        {
            final = TransferStage.Failed;
            failure = error is HttpRequestException ? "Falha de conexão" : "Não foi possível baixar a faixa";
            PlaybackDiagnostics.WriteFailure(DiagnosticSector.Storage, "Download de áudio", error);
        }
        finally
        {
            if (destination is not null)
            {
                try
                {
                    File.Delete(destination);
                    File.Delete(Path.ChangeExtension(destination, ".cover"));
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    PlaybackDiagnostics.WriteFailure(DiagnosticSector.Storage, "Limpeza de download", error);
                }
            }
            if (job.CancelRequested)
            {
                try { AudioDownload.DiscardPartial(directory, job.Track.Source); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                { PlaybackDiagnostics.WriteFailure(DiagnosticSector.Storage, "Descartar parcial", error); }
            }
            if (acquired) _slots.Release();
            job.Cancellation = null;
            job.ChangeStage(final, failure);
        }
    }
}
