using System.Text.Json;
using Hibike.Core.Models;
namespace Hibike.Core.Services;

public sealed class LibraryStore(string directory)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path = Path.Combine(directory, "favorites.json");
    public async Task<List<Track>> LoadAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (!File.Exists(_path)) return [];
            var tracks = JsonSerializer.Deserialize<List<Track>>(await File.ReadAllTextAsync(_path)) ?? [];
            if (tracks.Any(t => t is null || string.IsNullOrWhiteSpace(t.Source)))
                throw new InvalidDataException("Biblioteca contém faixa inválida.");
            return tracks;
        }
        catch (JsonException) { return []; }
        finally { _gate.Release(); }
    }
    public async Task SaveAsync(IEnumerable<Track> tracks)
    {
        var snapshot = tracks.ToArray();
        await _gate.WaitAsync();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(snapshot));
            File.Move(temp, _path, true);
        }
        finally { _gate.Release(); }
    }
}
