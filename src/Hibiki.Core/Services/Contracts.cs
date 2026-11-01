using Hibiki.Core.Models;
namespace Hibiki.Core.Services;

public interface ICommandRunner
{
    Task<ProcessResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}
public interface IMusicCatalog
{
    Task<IReadOnlyList<Track>> SearchAsync(string query, CancellationToken cancellationToken);
    Task<AudioSource> ResolveAsync(Track track, CancellationToken cancellationToken);
}
public interface IAudioPlayer : IDisposable
{
    bool IsPlaying { get; }
    double Position { get; }
    double Duration { get; }
    double Volume { get; set; }
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
