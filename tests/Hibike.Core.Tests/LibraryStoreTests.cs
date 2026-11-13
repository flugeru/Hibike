using System.Text.Json;
using Hibike.Core.Models;
using Hibike.Core.Services;
using Xunit;

namespace Hibike.Core.Tests;

public sealed class LibraryStoreTests
{
    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("[null]")]
    [InlineData("[{\"Source\":\"\"}]")]
    public async Task UnreadableLibraryIsReportedAndNeverOverwritten(string contents)
    {
        var directory = Path.Combine(Path.GetTempPath(), "hibike-library-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "favorites.json");
            await File.WriteAllTextAsync(path, contents);
            var store = new LibraryStore(directory);
            var load = await Record.ExceptionAsync(() => store.LoadAsync());
            Assert.True(load is JsonException or InvalidDataException);
            var save = await Record.ExceptionAsync(() => store.SaveAsync([]));
            Assert.True(save is JsonException or InvalidDataException);
            Assert.Equal(contents, await File.ReadAllTextAsync(path));
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task InvalidSnapshotDoesNotReplaceValidLibrary()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hibike-library-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LibraryStore(directory);
            var track = new Track("a", "Song", "Artist", "https://example.org/a");
            await store.SaveAsync([track]);
            await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync([track with { Source = "" }]));
            Assert.Equal(track, Assert.Single(await store.LoadAsync()));
        }
        finally { Directory.Delete(directory, true); }
    }
}
