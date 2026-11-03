using Hibike.Core.Services;
namespace Hibike.App;
public static class AppServices
{
    public static Func<Task<YouTubeAccountLibrary>>? ConnectYouTubeAccount { get; set; }
    private static IMusicCatalog? _catalog;
    public static Action? OpenPlayerView { get; set; }
    public static Action<Action<long>>? RequestDisplayFrame { get; set; }
    public static void InvalidateCatalog() => (_catalog as CachedMusicCatalog)?.Clear();
    public static Func<bool>? BackHandler { get; set; }
    private static string? _directory;
    public static MusicController? Controller { get; private set; }
    public static void Suspend() { Controller?.SaveSession(); Controller?.CancelPlayback(); Player?.Pause(); Controller?.FlushPendingSaves(); }
    public static IAudioPlayer? Player { get; private set; }
    public static void Configure(IMusicCatalog catalog, IAudioPlayer player, string directory)
    { _catalog = new CachedMusicCatalog(catalog); Player = player; _directory = directory; }
    public static MusicController CreateController() => Controller ??= new(
        _catalog ?? throw new InvalidOperationException("Catálogo não configurado."),
        Player ?? throw new InvalidOperationException("Player não configurado."),
        new LibraryStore(_directory!), _directory!);
}
