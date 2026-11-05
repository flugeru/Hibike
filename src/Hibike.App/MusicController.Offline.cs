using Hibike.Core.Models;
using Hibike.Core.Services;

namespace Hibike.App;

public sealed partial class MusicController
{
    private const string OfflineFileName = "offline-tracks.json";
    private readonly LibraryStore _offlineStore;
    private readonly SemaphoreSlim _offlineStoreGate = new(1, 1);
    private DateTime _nextNetworkCheck;
    private string? _removingOfflinePath;
    public IReadOnlyList<Track> OfflineTracks { get; private set; } = [];
    public bool NetworkAvailable { get; private set; }
    public bool OfflineMode => _personal.OfflineMode;
    public bool CanUseOnline => !OfflineMode && AppServices.HasNetwork;

    private void ObserveConnectivity()
    {
        var now = DateTime.UtcNow;
        if (now < _nextNetworkCheck) return;
        _nextNetworkCheck = now.AddSeconds(2);
        var available = AppServices.HasNetwork;
        if (NetworkAvailable == available) return;
        NetworkAvailable = available;
        if (!available) StopOnlineDiscovery();
        else if (!OfflineMode && _playbackRecovery is not null) _playbackRetryAt = now;
        Changed?.Invoke();
    }

    public void SetOfflineMode(bool enabled)
    {
        if (OfflineMode == enabled || (!enabled && !AppServices.HasNetwork)) return;
        _personal.OfflineMode = enabled;
        if (enabled)
        {
            StopOnlineDiscovery();
            if (Pending is { IsLocal: false } || Current is { IsLocal: false } && Pending is null)
                PausePlayback();
        }
        PersistPersonalization();
    }

    private void StopOnlineDiscovery()
    {
        CancelSearch();
        CancelRadioRequest();
        _recommendationsNetworkRequest?.Cancel();
    }

    private Track? FindOfflineCopy(Track track) => OfflineTracks.FirstOrDefault(local =>
        local.DownloadedFrom == track.Source && CanPlayOffline(local));

    internal bool CanPlayOffline(Track track) => track.IsLocal ? track.Source != _removingOfflinePath && File.Exists(track.Source) : FindOfflineCopy(track) is not null;

    internal string? OfflineOrigin(Track track)
    {
        if (!track.IsLocal || !Path.IsPathFullyQualified(track.Source)) return null;
        string? parent;
        try { parent = Path.GetDirectoryName(Path.GetFullPath(track.Source)); }
        catch (ArgumentException) { return null; }
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(parent, Path.GetFullPath(Path.Combine(DataDirectory, "downloads")), comparison)) return "Download";
        return string.Equals(parent, Path.GetFullPath(Path.Combine(DataDirectory, "audio")), comparison) ? "Importada" : null;
    }

    private async Task InitializeOfflineTracksAsync()
    {
        await _offlineStoreGate.WaitAsync();
        try
        {
            var saved = await _offlineStore.LoadAsync();
            var candidates = saved.Concat(Favorites).Concat(Queue).Concat(History)
                .Concat(Playlists.SelectMany(playlist => playlist.Tracks)).Concat(Pins)
                .Concat(Current is { } current ? [current] : []).DistinctBy(track => track.Source).ToArray();
            var available = await Task.Run(() => candidates
                .Where(track => OfflineOrigin(track) is not null && File.Exists(track.Source)).ToArray());
            OfflineTracks = available;
            if (!available.SequenceEqual(saved)) await _offlineStore.SaveAsync(available);
        }
        finally { _offlineStoreGate.Release(); }
    }

    internal async Task SaveOfflineTrackAsync(Track track)
    {
        await _offlineStoreGate.WaitAsync();
        try
        {
            var tracks = OfflineTracks.Append(track).DistinctBy(item => item.Source).ToArray();
            await _offlineStore.SaveAsync(tracks);
            OfflineTracks = tracks;
        }
        finally { _offlineStoreGate.Release(); }
        Changed?.Invoke();
    }

    internal Task<AudioSource> ResolveDownloadAsync(Track track, CancellationToken token) => _catalog.ResolveAsync(track, token);

    internal bool CanRemoveOfflineTrack(Track track) => Pending?.Source != track.Source && (Current?.Source != track.Source || !LoadingAudio);

    internal async Task RemoveOfflineTrackAsync(Track track)
    {
        await _offlineStoreGate.WaitAsync();
        try
        {
            if (!CanRemoveOfflineTrack(track)) throw new InvalidOperationException("Aguarde a preparação do áudio para remover este arquivo.");
            if (!OfflineTracks.Contains(track) || OfflineOrigin(track) is null) return;
            _removingOfflinePath = track.Source;
            if (Current?.Source == track.Source)
            {
                _playGeneration++;
                _wantsPlayback = false;
                CancelPlayback();
                await _playGate.WaitAsync();
                try
                {
                    await Player.StopAsync();
                    Current = ActivePlaybackTrack = null;
                    CurrentSource = null;
                    _queueIndex = -1;
                    _restored = false;
                    _seekAfterLoad = null;
                }
                finally { _playGate.Release(); }
                Changed?.Invoke();
            }
            await Task.Run(() =>
            {
                File.Delete(track.Source);
                if (OfflineOrigin(track) != "Download") return;
                try { File.Delete(Path.ChangeExtension(track.Source, ".cover")); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                { PlaybackDiagnostics.WriteFailure(DiagnosticSector.Storage, "Remover capa baixada", error); }
            });
            OfflineTracks = OfflineTracks.Where(item => item.Source != track.Source).ToArray();
            foreach (var item in Favorites.Where(item => item.Source == track.Source).ToArray()) Favorites.Remove(item);
            foreach (var item in Queue.Where(item => item.Source == track.Source).ToArray()) Queue.Remove(item);
            foreach (var item in History.Where(item => item.Source == track.Source).ToArray()) History.Remove(item);
            _personal.Pins.RemoveAll(item => item.Source == track.Source);
            foreach (var playlist in Playlists) playlist.Tracks.RemoveAll(item => item.Source == track.Source);
            PersistPersonalization();
            await Task.WhenAll(_offlineStore.SaveAsync(OfflineTracks), _store.SaveAsync(Favorites));
            SetStatus("Arquivo removido do aparelho.");
        }
        finally { _removingOfflinePath = null; _offlineStoreGate.Release(); }
    }
}
