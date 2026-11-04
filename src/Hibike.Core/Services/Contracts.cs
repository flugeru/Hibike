using Hibike.Core.Models;
namespace Hibike.Core.Services;

public interface ICommandRunner
{
    Task<ProcessResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}
public interface IMusicCatalog
{
    Task<IReadOnlyList<Track>> BrowsePageAsync(string source, int page, CancellationToken token) => page == 0 ? SearchAsync(source, token) : Task.FromResult<IReadOnlyList<Track>>([]);
    Task<IReadOnlyList<Track>> RadioAsync(Track seed, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Track>>([]);
    Task<IReadOnlyList<Track>> SearchAsync(string query, CancellationToken cancellationToken);
    Task<AudioSource> ResolveAsync(Track track, CancellationToken cancellationToken);
}
public interface IAudioPlayer : IDisposable
{
    bool IsPlaying { get; }
    double Position { get; }
    double Duration { get; }
    double Volume { get; set; }
    AudioSource? PlaybackSource => null;
    event EventHandler? Ended;
    event EventHandler<string>? Failed;
    Task PlayAsync(AudioSource source, CancellationToken cancellationToken);
    void Pause();
    void Resume();
    void Seek(double seconds);
}

public interface IAudioPreparation
{
    Task BeginPreparationAsync(Track track, CancellationToken cancellationToken);
    void CancelPreparation();
}
