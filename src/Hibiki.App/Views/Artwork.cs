using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Hibiki.Core.Models;
namespace Hibiki.App.Views;

internal sealed class Artwork : Border
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(12) };
    private CancellationTokenSource? _cancel;
    private Bitmap? _bitmap;
    public Artwork(Track? track, double size)
    {
        Width = Height = size;
        CornerRadius = Ui.Radius(size > Ui.D("ThumbSize") ? "RadiusCover" : "RadiusThumb");
        BorderThickness = size > Ui.D("ThumbSize") ? new Thickness(0) : new Thickness(Ui.D("TrackBorder"));
        BorderBrush = Ui.B("BorderBrush");
        ClipToBounds = true;
        Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = [new GradientStop(Ui.Color("CoverStartBrush"), 0), new GradientStop(Ui.Color("CoverEndBrush"), 1)]
        };
        var panel = new Grid();
        panel.Children.Add(Icons.Make("wave", "CoverWaveBrush", Math.Max(Ui.D("IconSize"), size * 0.4)));
        var picture = new Image { Stretch = Stretch.UniformToFill }; panel.Children.Add(picture);
        Child = panel;
        if (track?.Thumbnail is { } url)
        {
            AttachedToVisualTree += async (_, _) =>
            {
                _cancel?.Dispose(); _cancel = new CancellationTokenSource(); var token = _cancel.Token;
                try
                {
                    using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
                    response.EnsureSuccessStatusCode();
                    if (response.Content.Headers.ContentLength is > 4_000_000) return;
                    using var input = await response.Content.ReadAsStreamAsync(token);
                    using var memory = new MemoryStream();
                    var buffer = new byte[8192];
                    int read;
                    while ((read = await input.ReadAsync(buffer, token)) > 0)
                    {
                        if (memory.Length + read > 4_000_000) return;
                        memory.Write(buffer, 0, read);
                    }
                    memory.Position = 0;
                    var bitmap = Bitmap.DecodeToWidth(memory, (int)Math.Max(128, size * 2));
                    if (token.IsCancellationRequested) { bitmap.Dispose(); return; }
                    _bitmap = bitmap; picture.Source = bitmap;
                }
                catch (Exception) { /* Artwork is optional; playback never depends on it. */ }
            };
        }
        DetachedFromVisualTree += (_, _) =>
        {
            _cancel?.Cancel(); picture.Source = null; _bitmap?.Dispose(); _bitmap = null;
        };
    }
}
