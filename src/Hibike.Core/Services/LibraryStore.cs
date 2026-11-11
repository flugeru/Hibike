using System.Text.Json;
using Hibike.Core.Models;
namespace Hibike.Core.Services;

public sealed class LibraryStore(string directory, string fileName = "favorites.json")
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path = Path.Combine(directory, fileName);
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
        if (!File.Exists(_path)) return [];
        var tracks = JsonSerializer.Deserialize<List<Track>>(await File.ReadAllTextAsync(_path))
            ?? throw new InvalidDataException("Biblioteca inválida.");
        Validate(tracks);
        return tracks;
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
            await ReadAsync();
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(snapshot));
            File.Move(temp, _path, true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            _gate.Release();
        }
    }
}
