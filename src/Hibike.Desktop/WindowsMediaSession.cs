using Avalonia.Controls;
using Avalonia.Threading;
using Hibike.App;
using Hibike.Core.Services;
using Windows.Media;
using Windows.Storage.Streams;

namespace Hibike.Desktop;

internal sealed class WindowsMediaSession : IDisposable
{
    private readonly MusicController _music;
    private readonly SystemMediaTransportControls _controls;
    private string? _metadataKey;
    private bool _disposed;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(12) };
    private InMemoryRandomAccessStream? _artworkStream;

    public WindowsMediaSession(Window window, MusicController music)
    {
        _music = music;
        var handle = window.TryGetPlatformHandle()?.Handle ?? throw new InvalidOperationException("Janela Windows indisponível.");
        _controls = SystemMediaTransportControlsInterop.GetForWindow(handle);
        _controls.IsPlayEnabled = _controls.IsPauseEnabled = true;
        _controls.IsNextEnabled = _controls.IsPreviousEnabled = true;
        _controls.ButtonPressed += ButtonPressed;
        _controls.PlaybackPositionChangeRequested += PositionRequested;
        music.Changed += Update; music.Tick += Update;
        Update();
    }

    private void ButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
        => Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                switch (args.Button)
                {
                    case SystemMediaTransportControlsButton.Play:
                        if (!_music.Player.IsPlaying) await _music.ToggleAsync(); break;
                    case SystemMediaTransportControlsButton.Pause:
                        if (_music.Player.IsPlaying) await _music.ToggleAsync(); break;
                    case SystemMediaTransportControlsButton.Next: await _music.NextAsync(); break;
                    case SystemMediaTransportControlsButton.Previous: await _music.PreviousAsync(); break;
                }
            }
            catch (Exception error) { PlaybackDiagnostics.Write("Mídia Windows", error.Message); }
        });

    private void PositionRequested(SystemMediaTransportControls sender, PlaybackPositionChangeRequestedEventArgs args)
        => Dispatcher.UIThread.Post(() =>
        {
            try { _music.Player.Seek(args.RequestedPlaybackPosition.TotalSeconds); }
            catch (Exception error) { PlaybackDiagnostics.Write("Mídia Windows", error.Message); }
        });

    private async void Update()
    {
        if (_disposed) return;
        try
        {
            var track = _music.Current;
            _controls.IsEnabled = track is not null;
            if (track is null) return;
            _controls.PlaybackStatus = _music.LoadingAudio ? MediaPlaybackStatus.Changing
                : _music.Player.IsPlaying ? MediaPlaybackStatus.Playing : MediaPlaybackStatus.Paused;
            var source = _music.CurrentSource;
            var title = source?.Title ?? track.Title;
            var artist = source?.Artist ?? track.Artist;
            var artwork = source?.Artwork ?? track.Thumbnail;
            var key = string.Join('\n', title, artist, track.Album, artwork);
            if (key != _metadataKey)
            {
                _metadataKey = key;
                RandomAccessStreamReference? thumbnail = null;
                if (Uri.TryCreate(artwork, UriKind.Absolute, out var imageUri))
                {
                    if (imageUri.IsFile)
                        thumbnail = RandomAccessStreamReference.CreateFromFile(await Windows.Storage.StorageFile.GetFileFromPathAsync(imageUri.LocalPath));
                    else if (imageUri.Scheme == "https")
                    {
                        try
                        {
                            using var response = await _http.GetAsync(imageUri, HttpCompletionOption.ResponseHeadersRead);
                            response.EnsureSuccessStatusCode();
                            if (response.Content.Headers.ContentLength is > 8_000_000) throw new IOException("Capa excede limite de 8 MB.");
                            using var input = await response.Content.ReadAsStreamAsync();
                            using var bytes = new MemoryStream();
                            var buffer = new byte[8192];
                            int count;
                            while ((count = await input.ReadAsync(buffer)) > 0)
                            {
                                if (bytes.Length + count > 8_000_000) throw new IOException("Capa excede limite de 8 MB.");
                                bytes.Write(buffer, 0, count);
                            }
                            if (_disposed || _metadataKey != key) return;
                            var stream = new InMemoryRandomAccessStream();
                            using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
                            {
                                writer.WriteBytes(bytes.ToArray()); await writer.StoreAsync(); await writer.FlushAsync();
                            }
                            if (_disposed || _metadataKey != key) { stream.Dispose(); return; }
                            stream.Seek(0);
                            _artworkStream?.Dispose(); _artworkStream = stream;
                            thumbnail = RandomAccessStreamReference.CreateFromStream(stream);
                        }
                        catch (Exception error) { PlaybackDiagnostics.Write("Capa Windows", error.Message); }
                    }
                }
                if (_disposed || _metadataKey != key || _music.Current != track) return;
                var display = _controls.DisplayUpdater;
                display.ClearAll(); display.Type = MediaPlaybackType.Music;
                display.AppMediaId = "Hibike";
                display.MusicProperties.Title = title;
                display.MusicProperties.Artist = artist;
                display.MusicProperties.AlbumTitle = track.Album ?? "";
                display.Thumbnail = thumbnail;
                display.Update(); _metadataKey = key;
            }
            var duration = Math.Max(0, _music.Player.Duration > 0 ? _music.Player.Duration : track.DurationSeconds);
            _controls.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties
            {
                StartTime = TimeSpan.Zero, MinSeekTime = TimeSpan.Zero,
                EndTime = TimeSpan.FromSeconds(duration), MaxSeekTime = TimeSpan.FromSeconds(duration),
                Position = TimeSpan.FromSeconds(Math.Clamp(_music.Player.Position, 0, duration))
            });
        }
        catch (Exception error) { PlaybackDiagnostics.Write("Mídia Windows", error.Message); }
    }

    public void Dispose()
    {
        _disposed = true;
        _music.Changed -= Update; _music.Tick -= Update;
        _controls.ButtonPressed -= ButtonPressed;
        _controls.PlaybackPositionChangeRequested -= PositionRequested;
        _http.Dispose(); _artworkStream?.Dispose();
        _controls.PlaybackStatus = MediaPlaybackStatus.Closed;
        _controls.DisplayUpdater.ClearAll(); _controls.DisplayUpdater.Update(); _controls.IsEnabled = false;
    }
}
