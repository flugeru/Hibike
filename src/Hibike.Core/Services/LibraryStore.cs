using System.Text.Json;
using Hibike.Core.Models;
namespace Hibike.Core.Services;

public sealed class LibraryStore(string directory, string fileName = "favorites.json")
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path = Path.Combine(directory, fileName);
    // Length and timestamp of the file as last parsed or written by this store. When unchanged,
    // the protective pre-save read cannot find anything new, so a save need not parse the whole
    // library again. Any external change still triggers full validation before replacement.
    private (long Length, DateTime WriteTimeUtc)? _verified;
    public async Task<List<Track>> LoadAsync()
    {
        await _gate.WaitAsync();
        try
        {
            return await ReadAsync();
        }
        finally
        {
            try { File.Delete(_path + ".tmp"); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            _gate.Release();
        }
    }
    private async Task<List<Track>> ReadAsync()
    {
        _verified = null;
        if (!File.Exists(_path)) return [];
        var state = FileState();
        var tracks = JsonSerializer.Deserialize<List<Track>>(await File.ReadAllTextAsync(_path))
            ?? throw new InvalidDataException("Biblioteca inválida.");
        Validate(tracks);
        if (state is not null && state == FileState()) _verified = state;
        return tracks;
    }
    private (long, DateTime)? FileState()
    {
        var info = new FileInfo(_path);
        return info.Exists ? (info.Length, info.LastWriteTimeUtc) : null;
    }
    private static void Validate(IEnumerable<Track> tracks)
    {
        if (tracks.Any(t => t is null || string.IsNullOrWhiteSpace(t.Source)))
            throw new InvalidDataException("Biblioteca contém faixa inválida.");
    }
    public async Task SaveAsync(IEnumerable<Track> tracks)
    {
        var snapshot = tracks.ToArray();
        Validate(snapshot);
        await _gate.WaitAsync();
        var temp = _path + ".tmp";
        try
        {
            // Never replace unreadable user data with an empty or partial in-memory library.
            if (_verified is null || _verified != FileState()) await ReadAsync();
            _verified = null;
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot);
                await stream.FlushAsync();
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, _path, true);
            _verified = FileState();
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            _gate.Release();
        }
    }
}
