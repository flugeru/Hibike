using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using System.Runtime.InteropServices;
using Avalonia.Threading;
using Hibiki.Core.Models;
namespace Hibiki.App.Views;

internal sealed class Artwork : Border
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(12) };
    private CancellationTokenSource? _cancel;
    private Bitmap? _bitmap;
    public event Action<Color>? DominantColorReady;
    // A modest sampling grid is enough for an ambient *dark* background.
    // If decoding uses an unsupported pixel format, the normal theme gradient stays.
    private static Color? TryDominantColor(Bitmap bitmap)
    {
        if (bitmap.Format?.BitsPerPixel != 32) return null;
        var w = bitmap.PixelSize.Width;
        var h = bitmap.PixelSize.Height;
        if (w < 1 || h < 1 || w > 2048 || h > 2048) return null;
        var stride = checked(w * 4);
        var size = checked(stride * h);
        var pointer = Marshal.AllocHGlobal(size);
        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, w, h), pointer, size, stride);
            var bytes = new byte[size];
            Marshal.Copy(pointer, bytes, 0, size);
            double red = 0, green = 0, blue = 0, weightTotal = 0;
            for (var y = h / 8; y < h * 7 / 8; y += Math.Max(1, h / 20))
            for (var x = w / 8; x < w * 7 / 8; x += Math.Max(1, w / 20))
            {
                var p = y * stride + x * 4;
                var b = bytes[p]; var g = bytes[p + 1]; var r = bytes[p + 2];
                var brightness = (r + g + b) / 3.0;
                if (brightness < 20 || brightness > 238) continue;
                var saturation = Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));
                var weight = 1 + saturation / 64.0;
                red += r * weight; green += g * weight; blue += b * weight;
                weightTotal += weight;
            }
            if (weightTotal <= 0) return null;
            return Color.FromRgb((byte)(red / weightTotal),
                (byte)(green / weightTotal), (byte)(blue / weightTotal));
        }
        catch (Exception ex) when (ex is NotSupportedException or ArgumentException or OverflowException)
        {
            return null;
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }

    public static Color Darkened(Color dominant)
    {
        var strength = Ui.D("DominantDarken");
        var baseColor = Ui.Color("PlayerGradientBottomBrush");
        static byte Blend(byte from, byte to, double factor) =>
            (byte)Math.Clamp((int)Math.Round(from * (1 - factor) + to * factor), 0, 255);
        return Color.FromRgb(Blend(baseColor.R, dominant.R, strength),
            Blend(baseColor.G, dominant.G, strength),
            Blend(baseColor.B, dominant.B, strength));
    }

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
                    if (TryDominantColor(bitmap) is { } dominant)
                        DominantColorReady?.Invoke(dominant);
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
