using System.Text.Json;
using Hibike.Core.Models;
using Hibike.Core.Services;

namespace Hibike.App;

public sealed partial class MusicController
{
    public sealed record OfflineTrack(Track Track, string FileName);

    private const long OfflineLimit = 1_000_000_000;
    private string OfflineDirectory => Path.Combine(DataDirectory, "downloads");
    private CancellationTokenSource? _offlineDownload;
    public bool DownloadingOffline => _offlineDownload is not null;
    public EqualizerPreset Equalizer
    {
        get => _personal.Equalizer;
        set { _personal.Equalizer = value; PersistPersonalization(); }
    }
    public bool NormalizeAudio
    {
        get => _personal.NormalizeAudio;
        set { _personal.NormalizeAudio = value; PersistPersonalization(); }
    }

    public IReadOnlyList<Track> OfflineTracks => _personal.Downloads.Values.Where(entry => entry?.Track is not null && GetOfflineFile(entry.Track) is not null).Select(entry => entry.Track).ToArray();

    public string? GetOfflineFile(Track track)
    {
        if (!_personal.Downloads.TryGetValue(track.Source, out var saved) || saved is null || string.IsNullOrWhiteSpace(saved.FileName)
            || saved.FileName != Path.GetFileName(saved.FileName)) return null;
        var path = Path.Combine(OfflineDirectory, saved.FileName);
        return File.Exists(path) ? path : null;
    }

    private double? DownloadedGain(Track track) => _personal.Downloads.GetValueOrDefault(track.Source)?.Track.ReplayGainDb;

    public async Task DownloadOfflineAsync(Track track, IProgress<string> progress)
    {
        if (track.IsLocal || GetOfflineFile(track) is not null || DownloadingOffline) return;
        using var request = new CancellationTokenSource(TimeSpan.FromMinutes(30));
        _offlineDownload = request;
        try
        {
            var used = await Task.Run(() => Directory.Exists(OfflineDirectory)
                ? Directory.EnumerateFiles(OfflineDirectory).Sum(path => new FileInfo(path).Length) : 0L, request.Token);
            var source = await _catalog.ResolveAsync(track, request.Token);
            var path = await AudioDownload.SaveAsync(source, OfflineDirectory, progress, request.Token, OfflineLimit - used);
            _personal.Downloads[track.Source] = new(track with { ReplayGainDb = track.ReplayGainDb ?? source.GainDb }, Path.GetFileName(path));
            PersistPersonalization();
            SetStatus("Faixa disponível offline.");
        }
        finally { _offlineDownload = null; Changed?.Invoke(); }
    }

    public void CancelOfflineDownload() => _offlineDownload?.Cancel();

    public void DeleteOffline(Track track)
    {
        if (track.Source == Current?.Source || track.Source == Pending?.Source)
            throw new InvalidOperationException("Troque de faixa antes de apagar este download.");
        if (GetOfflineFile(track) is { } file) File.Delete(file);
        _personal.Downloads.Remove(track.Source);
        PersistPersonalization();
    }

    private sealed record LibraryBackup(int Version, Track[] Favorites, Personalization Preferences);
    private static bool IsPortableTrack(Track? track) => track is { IsLocal: false }
        && Uri.TryCreate(track.Source, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    public Task ExportLibraryAsync(Stream stream)
    {
        var preferences = PersonalSnapshot();
        preferences.Downloads.Clear();
        preferences.Pins.RemoveAll(track => !IsPortableTrack(track));
        foreach (var playlist in preferences.Playlists) playlist.Tracks.RemoveAll(track => !IsPortableTrack(track));
        return JsonSerializer.SerializeAsync(stream, new LibraryBackup(1, Favorites.Where(IsPortableTrack).ToArray(), preferences));
    }

    public async Task ImportLibraryAsync(Stream stream)
    {
        using var data = new MemoryStream();
        var buffer = new byte[16384];
        int count;
        while ((count = await stream.ReadAsync(buffer)) > 0)
        {
            if (data.Length + count > 8_000_000) throw new InvalidDataException("Backup excede 8 MB.");
            data.Write(buffer, 0, count);
        }
        var backup = JsonSerializer.Deserialize<LibraryBackup>(data.ToArray());
        if (backup is not { Version: 1, Favorites: not null, Preferences: not null })
            throw new InvalidDataException("Formato de backup desconhecido.");
        ValidatePersonalization(backup.Preferences);
        if (backup.Favorites.Any(track => !IsPortableTrack(track)) || backup.Preferences.Pins.Any(track => !IsPortableTrack(track))
            || backup.Preferences.Playlists.Any(playlist => playlist.Tracks.Any(track => !IsPortableTrack(track))))
            throw new InvalidDataException("O backup contém referências de faixa incompatíveis.");
        var merged = Favorites.Concat(backup.Favorites).DistinctBy(track => track.Source).ToArray();
        await _store.SaveAsync(merged);
        foreach (var track in merged)
            if (!Favorites.Any(existing => existing.Source == track.Source)) Favorites.Add(track);
        var restored = backup.Preferences;
        restored.Downloads = _personal.Downloads;
        restored.Pins = _personal.Pins.Concat(restored.Pins).DistinctBy(track => track.Source).ToList();
        foreach (var playlist in _personal.Playlists)
        {
            var match = restored.Playlists.FirstOrDefault(item => string.Equals(item.Name, playlist.Name, StringComparison.OrdinalIgnoreCase));
            if (match is null) restored.Playlists.Add(playlist);
            else match.Tracks = playlist.Tracks.Concat(match.Tracks).DistinctBy(track => track.Source).ToList();
        }
        foreach (var (key, stat) in _personal.Listening)
            if (!restored.Listening.TryGetValue(key, out var existing) || existing.LastPlayed < stat.LastPlayed)
                restored.Listening[key] = stat;
        _personal = restored;
        _homeUpdated = DateTime.MinValue;
        SetAutoplay(restored.Autoplay);
        await FlushPendingSavesAsync();
        SetStatus("Backup importado.");
    }
}
