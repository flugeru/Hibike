using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Hibiki.Core.Models;
using System.Runtime.InteropServices;
namespace Hibiki.App.Views;

internal sealed class Artwork : Border
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(12) };
    private CancellationTokenSource? _cancel;
    private Bitmap? _bitmap;
    public Artwork(Track? track, double size, Action<Color>? onDominantColor = null)
    {
        Width = Height = size; CornerRadius = Ui.Radius(size > 100 ? "CoverRadius" : "ThumbRadius"); ClipToBounds = true;
        BorderThickness = new Thickness(1); BorderBrush = Ui.Brush("Outline");
        Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = [new GradientStop(Ui.Color("PlaceholderStart"), 0), new GradientStop(Ui.Color("PlaceholderEnd"), 1)]
        };
        var panel = new Grid();
        panel.Children.Add(Icons.Make("wave", Ui.Brush("TextTertiary"), Math.Max(24, size * 0.4)));
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
                    if (onDominantColor is not null)
                    {
                        try { onDominantColor(DominantColor(bitmap)); }
                        catch (Exception) { /* Color sampling must never interrupt artwork or playback. */ }
                    }
                }
                catch (Exception) { /* Artwork is optional; playback never depends on it. */ }
            };
        }
        DetachedFromVisualTree += (_, _) =>
        {
            _cancel?.Cancel(); picture.Source = null; _bitmap?.Dispose(); _bitmap = null;
        };
    }

    private static Color DominantColor(Bitmap bitmap)
    {
        var size = new PixelSize(16, 16);
        using var sample = bitmap.CreateScaledBitmap(size, BitmapInterpolationMode.LowQuality);
        using var target = new WriteableBitmap(size, new Vector(96, 96), PixelFormats.Rgba8888, AlphaFormat.Unpremul);
        using var frame = target.Lock();
        sample.CopyPixels(frame);
        var pixels = new byte[frame.RowBytes * size.Height];
        Marshal.Copy(frame.Address, pixels, 0, pixels.Length);
        var bins = new Dictionary<int, int>();
        for (var y = 0; y < size.Height; y++)
        for (var x = 0; x < size.Width; x++)
        {
            var i = y * frame.RowBytes + x * 4;
            if (pixels[i + 3] < 128) continue;
            var key = (pixels[i] >> 5 << 6) | (pixels[i + 1] >> 5 << 3) | (pixels[i + 2] >> 5);
            bins[key] = bins.GetValueOrDefault(key) + 1;
        }
        if (bins.Count == 0) return Ui.Color("BackgroundColor");
        var dominant = bins.MaxBy(pair => pair.Value).Key;
        // Keep even a white cover dark enough for the light interface text.
        byte Dark(int component) => (byte)(12 + component * 0.17);
        return Color.FromRgb(Dark(((dominant >> 6) & 7) * 32 + 16),
            Dark(((dominant >> 3) & 7) * 32 + 16), Dark((dominant & 7) * 32 + 16));
    }
}
