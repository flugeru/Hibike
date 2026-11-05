using Hibike.Core.Models;
using Hibike.Core.Services;

namespace Hibike.App;

public sealed partial class MusicController
{
    private static readonly TimeSpan RecoveryInterval = TimeSpan.FromSeconds(30);
    private const string OfflineStatus = "Sem conexão com a internet. A faixa será retomada quando a rede voltar.";
    private sealed record PlaybackRecovery(Track Track, int? QueueIndex, Track? Source, double Position);
    private PlaybackRecovery? _playbackRecovery;
    private DateTime _playbackRetryAt;
    private DateTime? _bufferingSince;
    private double _lastPlaybackPosition;
    private bool _wantsPlayback;
    private int _playGeneration;
    private string? _playbackRecoveryMessage;
    public string? PlaybackRecoveryMessage => _wantsPlayback && !LoadingAudio && _playbackRecovery is not null
        ? _playbackRecoveryMessage : null;

    private PlaybackRecovery CaptureRecovery(Track track, int? queueIndex, Track? source)
    {
        var position = Current?.Source != track.Source ? 0
            : _seekAfterLoad ?? (Player.Position > 0 ? Player.Position : _lastPlaybackPosition);
        return new(track, queueIndex, source, Math.Max(0, position));
    }

    private void ScheduleRecovery(PlaybackRecovery recovery)
    {
        if (!_wantsPlayback || recovery.Track.IsLocal) return;
        _playbackRecovery = recovery;
        _playbackRetryAt = DateTime.UtcNow + RecoveryInterval;
        _bufferingSince = null;
        if (_catalog is CachedMusicCatalog cache) cache.Invalidate(recovery.Source ?? recovery.Track);
        _playbackRecoveryMessage = AppServices.HasNetwork
            ? "A reprodução foi interrompida. Nova tentativa em 30 segundos."
            : OfflineStatus;
        Fail(_playbackRecoveryMessage);
    }

    private void ObservePlaybackRecovery(DateTime now)
    {
        if (!_wantsPlayback || LoadingAudio) return;
        if (Player.IsPlaying)
        {
            _lastPlaybackPosition = _seekAfterLoad ?? Player.Position;
            _bufferingSince = null;
            return;
        }
        if (_playbackRecovery is not null)
        {
            if (now >= _playbackRetryAt) _ = RetryPlaybackAsync();
            return;
        }
        if (Current is not { IsLocal: false } track || !Player.IsBuffering)
        {
            _bufferingSince = null;
            return;
        }

        _bufferingSince ??= now;
        if (now - _bufferingSince.Value < RecoveryInterval) return;
        ScheduleRecovery(CaptureRecovery(track, CurrentQueueIndex, ActivePlaybackTrack));
        _ = RetryPlaybackAsync();
    }

    private Task RetryPlaybackAsync()
    {
        if (_playbackRecovery is not { } recovery || LoadingAudio || !_wantsPlayback) return Task.CompletedTask;
        if (!AppServices.HasNetwork)
        {
            _playbackRetryAt = DateTime.UtcNow + RecoveryInterval;
            if (_playbackRecoveryMessage != OfflineStatus)
            {
                _playbackRecoveryMessage = OfflineStatus;
                Fail(OfflineStatus);
            }
            return Task.CompletedTask;
        }

        PlaybackDiagnostics.Write("Reprodução", "Tentando retomar a faixa interrompida.");
        return PlayCoreAsync(recovery.Track, recovery.QueueIndex, recovery);
    }

    private void HandlePlaybackFailure(string message)
    {
        if (_playbackRecovery is not null) return;
        if (Current is { IsLocal: false } track && _wantsPlayback)
            ScheduleRecovery(CaptureRecovery(track, CurrentQueueIndex, ActivePlaybackTrack));
        else Fail(message);
    }

    public void PausePlayback()
    {
        var recovery = _playbackRecovery;
        if (recovery is null && Player.IsBuffering && Current is { IsLocal: false } track)
            recovery = CaptureRecovery(track, CurrentQueueIndex, ActivePlaybackTrack);
        _wantsPlayback = false;
        CancelPlayback();
        _playbackRecovery = recovery;
        try { Player.Pause(); SetStatus("Pausado"); }
        catch (Exception error) { Fail(error.Message); }
    }

    public async Task ResumePlaybackAsync()
    {
        if (LoadingAudio || Player.IsPlaying) return;
        _wantsPlayback = true;
        try
        {
            if (_playbackRecovery is not null) { await RetryPlaybackAsync(); return; }
            if (Current is { } track && _restored)
            {
                await PlayCoreAsync(track, CurrentQueueIndex, new(track, CurrentQueueIndex, null, _resumePosition));
                return;
            }
            if (Current is { IsLocal: false } stalled && (Player.IsBuffering || HasError || !AppServices.HasNetwork))
            {
                ScheduleRecovery(CaptureRecovery(stalled, CurrentQueueIndex, ActivePlaybackTrack));
                await RetryPlaybackAsync();
                return;
            }
            if (Current is not null) Player.Resume();
            else if (Queue.Count > 0) { await PlayAsync(Queue[0], 0); return; }
            else { _wantsPlayback = false; SetStatus("Escolha uma faixa ou abra um arquivo de áudio."); return; }
            SetStatus("Reprodução retomada.");
        }
        catch (Exception ex) { Fail(ex.Message); }
    }
}
