using Hibiki.Core.Services;
namespace Hibiki.App;
public static class AppServices
{
    private static IMusicCatalog? _catalog;
    private static string? _directory;
    public static MusicController? Controller { get; private set; }
    public static void Suspend() { Controller?.CancelPlayback(); Player?.Pause(); }
    public static IAudioPlayer? Player { get; private set; }
    public static void Configure(IMusicCatalog catalog, IAudioPlayer player, string directory)
    { _catalog = catalog; Player = player; _directory = directory; }
    public static MusicController CreateController() => Controller = new(
        _catalog ?? throw new InvalidOperationException("Catálogo não configurado."),
        Player ?? throw new InvalidOperationException("Player não configurado."),
        new LibraryStore(_directory!), _directory!);
}
