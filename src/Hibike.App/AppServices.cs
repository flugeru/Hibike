using System.Net.NetworkInformation;
using Hibike.Core.Services;
namespace Hibike.App;
public static class AppServices
{
    public static Func<Hibike.Core.Models.Track, string, Hibike.Core.Models.Track>? ReadLocalMetadata { get; set; }
    public static Hibike.Core.Models.Track ReadLocalTrack(Hibike.Core.Models.Track track, string cache) =>
        ReadLocalMetadata?.Invoke(track, cache) ?? LocalTrackMetadata.Read(track, cache);
    public static Func<string, Task<IReadOnlyDictionary<string, string>>>? ReadMediaInfo { get; set; }
    public static IReadOnlyList<string>? AudioEngines { get; set; }
    public static string? SelectedAudioEngine { get; set; }
    public static Func<string, bool>? AudioEngineAvailable { get; set; }
    public static Action<string>? SaveAudioEngine { get; set; }
    public static Func<Task<YouTubeAccountLibrary>>? ConnectYouTubeAccount { get; set; }
    public static Func<Task<YouTubeAccountLibrary?>>? RestoreYouTubeAccount { get; set; }
    public static Action<YouTubeAccountLibrary>? SaveYouTubeAccount { get; set; }
    public static Action? ClearYouTubeAccount { get; set; }
    public static (Func<string?> Read, Action<string> Save, Action Clear, Func<bool> Exists)? DiscordTokenStorage { get; set; }
    private static IMusicCatalog? _catalog;
    public static Action? OpenPlayerView { get; set; }
    public static Func<bool>? IsNetworkAvailable { get; set; }
    private static long _networkSampleAt = long.MinValue;
    private static bool _desktopNetworkAvailable = true;
    internal static bool HasNetwork
    {
        get
        {
            if (IsNetworkAvailable is { } platform) return platform();
            var now = Environment.TickCount64;
            if (now >= _networkSampleAt)
            {
                _networkSampleAt = now + 2000;
                try { _desktopNetworkAvailable = NetworkInterface.GetIsNetworkAvailable(); }
                catch (NetworkInformationException) { }
                catch (PlatformNotSupportedException) { }
            }
            return _desktopNetworkAvailable;
        }
    }
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
