using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Hibike.App.Views;

/// <summary>Derives the "Cor da capa" theme colours from a small decoded cover.</summary>
internal static class CoverPalette
{
    internal readonly record struct Colors(string Accent, Color Tint);

    /// <summary>
    /// Dominant hue weighted by saturation and brightness, so a vivid detail beats a large grey
    /// area. Returns null for monochrome covers, which keep the monochrome accent.
    /// </summary>
    public static Colors? From(Bitmap bitmap)
    {
        var size = bitmap.PixelSize;
        if (size.Width <= 0 || size.Height <= 0) return null;
        using var target = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        var pixels = new byte[size.Width * size.Height * 4];
        using (var frame = target.Lock())
        {
            bitmap.CopyPixels(frame);
            for (var row = 0; row < size.Height; row++)
                Marshal.Copy(frame.Address + row * frame.RowBytes, pixels, row * size.Width * 4, size.Width * 4);
        }
        return FromBgra(pixels);
    }

    internal static Colors? FromBgra(byte[] bgra)
    {
        double x = 0, y = 0, weight = 0, saturation = 0;
        for (var i = 0; i + 3 < bgra.Length; i += 4)
        {
            if (bgra[i + 3] < 128) continue;
            var (h, s, v) = Hsv(bgra[i + 2], bgra[i + 1], bgra[i]);
            var w = s * s * v;
            if (w <= 0) continue;
            var angle = h * Math.PI / 180;
            x += Math.Cos(angle) * w; y += Math.Sin(angle) * w;
            weight += w; saturation += s * w;
        }
        // Too little colour to theme by: keep the monochrome look.
        if (weight < bgra.Length / 4 * .02) return null;
        var hue = (Math.Atan2(y, x) * 180 / Math.PI + 360) % 360;
        var sat = Math.Clamp(saturation / weight, .45, .8);
        // Light enough for dark glyphs on the play button and legible on black.
        var accent = FromHsl(hue, sat, .74);
        var tint = FromHsl(hue, sat * .7, .17);
        return new(accent.ToString(), tint);
    }

    private static (double H, double S, double V) Hsv(byte r, byte g, byte b)
    {
        double rf = r / 255d, gf = g / 255d, bf = b / 255d;
        var max = Math.Max(rf, Math.Max(gf, bf)); var min = Math.Min(rf, Math.Min(gf, bf));
        var delta = max - min;
        var h = delta == 0 ? 0 : max == rf ? 60 * (((gf - bf) / delta) % 6) : max == gf ? 60 * ((bf - rf) / delta + 2) : 60 * ((rf - gf) / delta + 4);
        return ((h + 360) % 360, max == 0 ? 0 : delta / max, max);
    }

    internal static Color FromHsl(double h, double s, double l)
    {
        var c = (1 - Math.Abs(2 * l - 1)) * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = l - c / 2;
        var (r, g, b) = h switch
        {
            < 60 => (c, x, 0d), < 120 => (x, c, 0d), < 180 => (0d, c, x),
            < 240 => (0d, x, c), < 300 => (x, 0d, c), _ => (c, 0d, x)
        };
        static byte Byte(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
        return Color.FromRgb(Byte(r + m), Byte(g + m), Byte(b + m));
    }
}
