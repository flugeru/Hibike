successfully downloaded text file (SHA: 2125f4605841db02228b7eaf81b7064dbf7834fc)
using Hibike.Core.Models;
using Hibike.Core.Services;

namespace Hibike.App;

public sealed partial class MusicController
{
    private static readonly TimeSpan RecoveryInterval = TimeSpan.FromSeconds(15);
    private const int MaxRecoveryAttempts = 3;
    private const string OfflineStatus = "Sem conexão com a internet. A faixa será retomada quando a rede voltar.";
    private sealed record PlaybackRecovery(Track Track, int? QueueIndex, Track? Source, double Position, int Attempts = 0);
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

    private void ClearRecovery()
    {
        _playbackRecovery = null;
        _playbackRecoveryMessage = null;
        _bufferingSince = null;
    }

    private static bool IsPermanentPlaybackFailure(Exception error)
    {
        if (error is System.Text.Json.JsonException or InvalidDataException or NotSupportedException or ArgumentException)
            return true;
        if (error is HttpRequestException { StatusCode: { } status })
            return (int)status is >= 400 and < 500 and not 408 and not 429;
        var message = error.Message;
        return message.Contains("não encontrou áudio reproduzível", StringComparison.OrdinalIgnoreCase)
            || message.Contains("formato", StringComparison.OrdinalIgnoreCase)
            || message.Contains("login required", StringComparison.OrdinalIgnoreCase)
            || message.Contains("sign in", StringComparison.OrdinalIgnoreCase)
            || message.Contains("private video", StringComparison.OrdinalIgnoreCase)
            || message.Contains("video unavailable", StringComparison.OrdinalIgnoreCase);
    }

    private void ScheduleRecovery(PlaybackRecovery recovery)
    {
        if (!_wantsPlayback || recovery.Track.IsLocal) return;
        if (recovery.Attempts >= MaxRecoveryAttempts)
        {
            ClearRecovery();
            _wantsPlayback = false;
            Fail("Não foi possível retomar a reprodução após 3 tentativas.");
            return;
        }
        _playbackRecovery = recovery;
        var delay = TimeSpan.FromTicks(RecoveryInterval.Ticks * (1L << recovery.Attempts));
        _playbackRetryAt = DateTime.UtcNow + delay;
        _bufferingSince = null;
        if (_catalog is CachedMusicCatalog cache) cache.Invalidate(recovery.Source ?? recovery.Track);
        _playbackRecoveryMessage = AppServices.HasNetwork
            ? $"A reprodução foi interrompida. Nova tentativa em {(int)delay.TotalSeconds} segundos."
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
        if (!CanUseOnline)
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
        return PlayCoreAsync(recovery.Track, recovery.QueueIndex, recovery with { Attempts = recovery.Attempts + 1 });
    }

    private void HandlePlaybackFailure(string message)
    {
        if (_playbackRecovery is not null) return;
        if (Current is { IsLocal: false } track && _wantsPlayback)
            ScheduleRecovery(CaptureRecovery(track, CurrentQueueIndex, ActivePlaybackTrack));
        else Fail(message);
    }

    private void HandlePlaybackFailure(Exception error, PlaybackRecovery recovery)
    {
        if (IsPermanentPlaybackFailure(error))
        {
            ClearRecovery();
            _wantsPlayback = false;
            Fail(error.Message);
        }
        else ScheduleRecovery(recovery);
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
        if (!CanUseOnline && (_playbackRecovery?.Track ?? Current) is { IsLocal: false } online)
        {
            if (FindOfflineCopy(online) is not null)
            {
                await PlayCoreAsync(online, CurrentQueueIndex, _playbackRecovery ?? CaptureRecovery(online, CurrentQueueIndex, null));
                return;
            }
            if (OfflineMode) { SetStatus("Escolha uma faixa disponível offline em Downloads."); return; }
        }
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
