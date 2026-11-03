using Avalonia.Controls;
using Avalonia.Media;
using Hibike.Core.Services;

namespace Hibike.App.Views;

public sealed partial class MainView
{
    private async Task ShowLyricsAsync()
    {
        if (_music.Current is not { } track) return;
        var body = new StackPanel { Spacing = 8 };
        var message = Text("Buscando letra no LRCLIB…", UiTheme.Body);
        message.TextWrapping = TextWrapping.Wrap;
        body.Children.Add(message);
        var scroll = UiTheme.FadingScroll(body);
        using var request = new CancellationTokenSource();
        var loading = true;
        var attached = true;
        Action? update = null;
        scroll.DetachedFromVisualTree += (_, _) =>
        {
            attached = false;
            if (update is not null) _music.Tick -= update;
            if (loading) request.Cancel();
        };
        ShowSheet("Letra · " + track.Title, scroll, bottom: true);
        try
        {
            var lyrics = await SongLyrics.LoadAsync(track, System.IO.Path.Combine(_music.DataDirectory, "lyrics"), request.Token);
            if (!attached) return;
            if (lyrics is null) { message.Text = "Letra indisponível para esta versão."; return; }
            var lines = SongLyrics.Parse(lyrics);
            if (lines.Count == 0) { message.Text = lyrics; return; }
            body.Children.Clear();
            var buttons = lines.Select(line =>
            {
                var button = ActionButton("", () =>
                {
                    if (_music.Current?.Source == track.Source && !_music.LoadingAudio) _music.Player.Seek(line.Seconds);
                    return Task.CompletedTask;
                });
                button.Content = new TextBlock { Text = string.IsNullOrWhiteSpace(line.Text) ? "♪" : line.Text, TextWrapping = TextWrapping.Wrap };
                button.HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left;
                return button;
            }).ToArray();
            foreach (var button in buttons) body.Children.Add(button);
            var active = -1;
            update = () =>
            {
                if (_music.Current?.Source != track.Source) return;
                var index = -1;
                var position = _music.Position;
                while (index + 1 < lines.Count && lines[index + 1].Seconds <= position) index++;
                if (index == active) return;
                if (active >= 0) buttons[active].Classes.Remove("primary");
                active = index;
                if (active >= 0) { buttons[active].Classes.Add("primary"); buttons[active].BringIntoView(); }
            };
            _music.Tick += update;
            update();
        }
        catch (OperationCanceledException) when (!attached) { }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or System.Text.Json.JsonException)
        { if (attached) message.Text = "Não foi possível carregar a letra. Tente abrir novamente."; }
        finally { loading = false; }
    }
}
